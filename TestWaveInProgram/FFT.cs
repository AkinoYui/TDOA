using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace TestWaveInProgram
{
    public static class FFT
    {
        public static void Radix2(ref Complex[] bin, bool forward)
        {
            //Check bin is power of two
            int length = bin.Length;
            if (!IsPowerOfTwo(length))
                throw new ArgumentException("Bin length is not power of 2.");

            //Re-order the bin
            BitReverseSorting(ref bin);

            //Begin butterfly computation
            Butterfly(ref bin, length, 0, forward);

            //Normalize for IFFT
            if (!forward)
                for (int i = 0; i < length; i++)
                    bin[i] /= length;
        }

        internal static Complex Wronskian(int k, int N)
        {
            return new Complex(Math.Cos(-2 * Math.PI * k / N), Math.Sin(-2 * Math.PI * k / N));
        }

        private static void Butterfly(ref Complex[] bin, int N, int stage, bool forward)
        {
            //Go up to the higher stage
            if (N > 2)
                Butterfly(ref bin, N / 2, stage + 1, forward);


            int sign = forward ? 1 : -1;    //Forward or inverse FFT
            int level = (1 << stage);       //Number of DFT for each stage
            Complex temp;

            for (int i = 0; i < level; i++)     //i-th DFT at the stage
                for (int k = 0; k < N / 2; k++)
                {
                    /*
                     * Original Code to help interpret the following code.
                    temp1 = bin[k + i * N] + Wronskian(k * sign, N) * bin[k + i * N + N / 2];
                    temp2 = bin[k + i * N] - Wronskian(k * sign, N) * bin[k + i * N + N / 2];

                    bin[k + i * N] = temp1;
                    bin[k + i * N + N / 2] = temp2;
                    */

                    temp = Wronskian(k * sign, N) * bin[k + i * N + N / 2];
                    bin[k + i * N + N / 2] = bin[k + i * N] - temp;
                    bin[k + i * N] += temp;
                }
        }

        private static void BitReverseSorting(ref Complex[] data)
        {
            //Calculate bin size
            int length = data.Length;
            int bits = 0;

            while ((length >>= 1) != 0)
                bits++;

            //Arrange
            Complex[] arranged = new Complex[data.Length];
            for (int i = 0; i < data.Length; i++)
                arranged[BitReverse(i, bits)] = data[i];

            data = arranged;
        }

        private static int BitReverse(int value, int bits)
        {
            int reversed = 0;

            while (value > 0)
            {
                reversed <<= 1;
                reversed ^= (value & 1);

                value >>= 1;

                bits--;
            }
            reversed <<= bits;

            return reversed;
        }

        private static bool IsPowerOfTwo(int length)
        {
            return ((length & (length - 1)) == 0) && length != 0;
        }

    }
}
