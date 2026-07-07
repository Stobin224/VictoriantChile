using System;

namespace VictoriantChile.Simulation
{
    /// <summary>
    /// Escala numérica canónica definida por CON-SIM-001.
    /// El estado guarda enteros: 1 punto visible equivale a 100 unidades internas.
    /// </summary>
    public static class FixedPoint
    {
        public const int Scale = 100;
        public const int OneHundred = 100 * Scale;

        public static int FromWhole(int value)
        {
            return checked(value * Scale);
        }

        public static int Clamp(int valueS, int minS, int maxS)
        {
            if (minS > maxS)
            {
                throw new ArgumentException("El mínimo no puede ser mayor que el máximo.");
            }

            return Math.Min(Math.Max(valueS, minS), maxS);
        }
    }
}
