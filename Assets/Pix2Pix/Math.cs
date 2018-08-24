#define ENABLE_COMPUTE

using System.Linq;

namespace Pix2Pix
{
    public struct Tensor
    {
        public int[] Shape;
        public float[] Data;

        public Tensor(int[] shape, float[] data)
        {
            Shape = shape;
            Data = data;
        }

        public Tensor(int[] shape)
        {
            Shape = shape;
            Data = new float[shape.Aggregate(1, (acc, x) => acc * x)];
        }

        public override string ToString()
        {
            return "Tensor " +
                string.Join("x", Shape.Select(x => x.ToString()).ToArray());
        }

        public float Get(int i0)
        {
            UnityEngine.Debug.Assert(Shape.Length == 1);
            if (i0 < 0 || i0 >= Shape[0]) return 0;
            return Data[i0];
        }

        public float Get(int i0, int i1)
        {
            UnityEngine.Debug.Assert(Shape.Length == 2);
            if (i0 < 0 || i0 >= Shape[0]) return 0;
            if (i1 < 0 || i1 >= Shape[1]) return 0;
            return Data[i0 * Shape[1] + i1];
        }

        public float Get(int i0, int i1, int i2)
        {
            UnityEngine.Debug.Assert(Shape.Length == 3);
            if (i0 < 0 || i0 >= Shape[0]) return 0;
            if (i1 < 0 || i1 >= Shape[1]) return 0;
            if (i2 < 0 || i2 >= Shape[2]) return 0;
            return Data[(i0 * Shape[1] + i1) * Shape[2] + i2];
        }

        public float Get(int i0, int i1, int i2, int i3)
        {
            UnityEngine.Debug.Assert(Shape.Length == 4);
            if (i0 < 0 || i0 >= Shape[0]) return 0;
            if (i1 < 0 || i1 >= Shape[1]) return 0;
            if (i2 < 0 || i2 >= Shape[2]) return 0;
            if (i3 < 0 || i3 >= Shape[3]) return 0;
            return Data[((i0 * Shape[1] + i1) * Shape[2] + i2) * Shape[3] + i3];
        }

        public void Set(int i0, float value)
        {
            UnityEngine.Debug.Assert(Shape.Length == 1);
            if (i0 < 0 || i0 >= Shape[0]) return;
            Data[i0] = value;
        }

        public void Set(int i0, int i1, float value)
        {
            UnityEngine.Debug.Assert(Shape.Length == 2);
            if (i0 < 0 || i0 >= Shape[0]) return;
            if (i1 < 0 || i1 >= Shape[1]) return;
            Data[i0 * Shape[1] + i1] = value;
        }

        public void Set(int i0, int i1, int i2, float value)
        {
            UnityEngine.Debug.Assert(Shape.Length == 3);
            if (i0 < 0 || i0 >= Shape[0]) return;
            if (i1 < 0 || i1 >= Shape[1]) return;
            if (i2 < 0 || i2 >= Shape[2]) return;
            Data[(i0 * Shape[1] + i1) * Shape[2] + i2] = value;
        }

        public void Set(int i0, int i1, int i2, int i3, float value)
        {
            UnityEngine.Debug.Assert(Shape.Length == 4);
            if (i0 < 0 || i0 >= Shape[0]) return;
            if (i1 < 0 || i1 >= Shape[1]) return;
            if (i2 < 0 || i2 >= Shape[2]) return;
            if (i3 < 0 || i3 >= Shape[3]) return;
            Data[((i0 * Shape[1] + i1) * Shape[2] + i2) * Shape[3] + i3] = value;
        }

        public static Tensor Relu(Tensor input)
        {
#if ENABLE_COMPUTE
            return GpuHelper.InvokeActivationKernel("Relu", input);
#else
            var data = new float[input.Data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                var v = input.Data[i];
                data[i] = v < 0 ? 0 : v;
            }
            return new Tensor(input.Shape, data);
#endif
        }

        public static Tensor LeakyRelu(Tensor input, float alpha)
        {
#if ENABLE_COMPUTE
            ComputeAssets.Activation.SetFloat("Alpha", alpha);
            return GpuHelper.InvokeActivationKernel("LeakyRelu", input);
#else
            var data = new float[input.Data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                var v = input.Data[i];
                data[i] = v < 0 ? v * alpha : v;
            }
            return new Tensor(input.Shape, data);
#endif
        }

        public static Tensor Tanh(Tensor input)
        {
#if ENABLE_COMPUTE
            return GpuHelper.InvokeActivationKernel("Tanh", input);
#else
            var data = new float[input.Data.Length];
            for (var i = 0; i < data.Length; i++)
                data[i] = (float)System.Math.Tanh(input.Data[i]);
            return new Tensor(input.Shape, data);
#endif
        }

        public static Tensor Concat(Tensor input1, Tensor input2)
        {
#if ENABLE_COMPUTE
            var elements = input1.Shape[0] * input1.Shape[1];
            var kernel = elements < 512 ? "Concat64" : "Concat512";
            if (elements < 64) kernel = "Concat4";
            return GpuHelper.InvokeConcatKernel(kernel, input1, input2);
#else
            UnityEngine.Debug.Assert(input1.Shape.Length == 3);
            UnityEngine.Debug.Assert(input2.Shape.Length == 3);
            UnityEngine.Debug.Assert(input1.Shape[0] == input2.Shape[0]);
            UnityEngine.Debug.Assert(input1.Shape[1] == input2.Shape[1]);

            var ch1 = input1.Shape[2];
            var ch2 = input2.Shape[2];

            var output = new Tensor(new [] {input1.Shape[0], input1.Shape[1], ch1 + ch2});

            for (var i = 0; i < input1.Shape[0] ; i++)
            {
                for (var j = 0; j < input1.Shape[1] ; j++)
                {
                    for (var k = 0; k < ch1; k++)
                    {
                        output.Set(i, j, k, input1.Get(i, j, k));
                    }
                    for (var k = 0; k < ch2; k++)
                    {
                        output.Set(i, j, ch1 + k, input2.Get(i, j, k));
                    }
                }
            }

            return output;
#endif
        }

        public static Tensor BatchNorm(Tensor input, Tensor scale, Tensor offset)
        {
#if ENABLE_COMPUTE
            var channels = scale.Shape[0];
            var kernel = channels == 512 ? "BatchNorm512" : "BatchNorm64";
            return GpuHelper.InvokeBatchNormKernel(kernel, input, scale, offset);
#else
            UnityEngine.Debug.Assert(input.Shape.Length == 3);

            var output = new Tensor(input.Shape);
            var epsilon = 1e-5f;

            for (var ch = 0; ch < input.Shape[2]; ch++)
            {
                var mean = 0.0f;
                for (var y = 0; y < input.Shape[0]; y++)
                    for (var x = 0; x < input.Shape[1]; x++)
                        mean += input.Get(y, x, ch);
                mean /= input.Shape[0] * input.Shape[1];

                var variance = 0.0f;
                for (var y = 0; y < input.Shape[0]; y++)
                    for (var x = 0; x < input.Shape[1]; x++)
                        variance += MathUtil.Square(input.Get(y, x, ch) - mean);
                variance /= input.Shape[0] * input.Shape[1];

                var offs = offset.Get(ch);
                var sc = scale.Get(ch);

                sc /= UnityEngine.Mathf.Sqrt(variance + epsilon);

                for (var y = 0; y < input.Shape[0]; y++)
                    for (var x = 0; x < input.Shape[1]; x++)
                        output.Set(y, x, ch, offs + (input.Get(y, x, ch) - mean) * sc);
            }

            return output;
#endif
        }

        public static Tensor Conv2D(Tensor input, Tensor filter, Tensor bias)
        {
#if ENABLE_COMPUTE
            var outChannels = filter.Shape[3];
            var kernel = outChannels >= 512 ? "Conv2D_512_1" : "Conv2D_64_8";
            return GpuHelper.InvokeConvolutionKernel(GpuHelper.ConvolutionMode.Down, kernel, input, filter, bias);
#else
            var inHeight = input.Shape[0];
            var inWidth = input.Shape[1];
            var inChannels = input.Shape[2];

            var outHeight = inHeight / 2;
            var outWidth = inWidth / 2;
            var outChannels = filter.Shape[3];

            var filterHeight = filter.Shape[0];
            var filterWidth = filter.Shape[1];

            var output = new Tensor(new [] {outHeight, outWidth, outChannels});

            for (var oc = 0; oc < outChannels; oc++)
            {
                for (var oy = 0; oy < outHeight; oy++)
                {
                    var ymin = oy * 2 - filterHeight / 2 + 1;

                    for (var ox = 0; ox < outWidth; ox++)
                    {
                        var xmin = ox * 2 - filterWidth / 2 + 1;
                        var prod = 0.0f;

                        for (var fy = 0; fy < filterHeight; fy++)
                        {
                            for (var fx = 0; fx < filterWidth; fx++)
                            {
                                for (var ic = 0; ic < inChannels; ic++)
                                {
                                    var pixel = input.Get(ymin + fy, xmin + fx, ic);
                                    var weight = filter.Get(fy, fx, ic, oc);
                                    prod += pixel * weight;
                                }
                            }
                        }

                        output.Set(oy, ox, oc, prod + bias.Get(oc));
                    }
                }
            }

            return output;
#endif
        }

        public static Tensor Deconv2D(Tensor input, Tensor filter, Tensor bias)
        {
#if ENABLE_COMPUTE
            var outChannels = filter.Shape[2];
            var kernel = outChannels >= 512 ? "TransConv2D_512_1" : "TransConv2D_64_8";
            if (outChannels == 3) kernel = "TransConv2D_3_128";
            return GpuHelper.InvokeConvolutionKernel(GpuHelper.ConvolutionMode.Up, kernel, input, filter, bias);
#else
            var inHeight = input.Shape[0];
            var inWidth = input.Shape[1];
            var inChannels = input.Shape[2];

            var outHeight = inHeight * 2;
            var outWidth = inWidth * 2;
            var outChannels = filter.Shape[2];

            var filterHeight = filter.Shape[0];
            var filterWidth = filter.Shape[1];

            var output = new Tensor(new [] {outHeight, outWidth, outChannels});

            for (var oc = 0; oc < outChannels; oc++)
            {
                for (var oy = 0; oy < outHeight; oy++)
                {
                    var ymin = (oy - 1) / 2;

                    for (var ox = 0; ox < outWidth; ox++)
                    {
                        var xmin = (ox - 1) / 2;
                        var prod = 0.0f;

                        for (var fy = oy % 2; fy < filterHeight; fy += 2)
                        {
                            for (var fx = ox % 2; fx < filterWidth; fx += 2)
                            {
                                for (var ic = 0; ic < inChannels; ic++)
                                {
                                    var pixel = input.Get(ymin + fy / 2, xmin + fx / 2, ic);
                                    var weight = filter.Get(
                                        filterHeight - 1 - fy,
                                        filterWidth  - 1 - fx,
                                        oc, ic
                                    );
                                    prod += pixel * weight;
                                }
                            }
                        }

                        output.Set(oy, ox, oc, prod + bias.Get(oc));
                    }
                }
            }

            return output;
#endif
        }
    }
}
