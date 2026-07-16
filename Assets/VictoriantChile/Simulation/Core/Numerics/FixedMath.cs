using System;

namespace VictoriantChile.Simulation.Core.Numerics
{
    public static class FixedMath
    {
        public const int Scale = 100;
        public const int HundredS = 10_000;
        public const int MultiplierBaseS = 10_000;

        public static long RoundDivide(long numerator, long positiveDenominator)
        {
            if (positiveDenominator == 0)
            {
                throw new DivideByZeroException("Denominator must be positive.");
            }

            if (positiveDenominator < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positiveDenominator), "Denominator must be positive.");
            }

            long quotient = numerator / positiveDenominator;
            long remainder = numerator % positiveDenominator;
            if (remainder == 0)
            {
                return quotient;
            }

            long magnitude = remainder < 0 ? -remainder : remainder;
            if (magnitude >= positiveDenominator - magnitude)
            {
                return checked(quotient + (numerator < 0 ? -1 : 1));
            }

            return quotient;
        }

        public static int RoundDivideToInt(long numerator, long positiveDenominator)
        {
            return checked((int)RoundDivide(numerator, positiveDenominator));
        }

        public static int FromWhole(int whole)
        {
            checked
            {
                return whole * Scale;
            }
        }

        public static int RoundToWhole(int scaled)
        {
            return RoundDivideToInt(scaled, Scale);
        }

        public static int AddChecked(int left, int right)
        {
            checked
            {
                return left + right;
            }
        }

        public static int MultiplyScaled(int valueS, int factorS)
        {
            long product = (long)valueS * factorS;
            return RoundDivideToInt(product, MultiplierBaseS);
        }

        public static int Clamp(int value, int minInclusive, int maxInclusive)
        {
            if (minInclusive > maxInclusive)
            {
                throw new ArgumentException("Minimum must be less than or equal to maximum.", nameof(minInclusive));
            }

            if (value < minInclusive)
            {
                return minInclusive;
            }

            if (value > maxInclusive)
            {
                return maxInclusive;
            }

            return value;
        }
    }
}
