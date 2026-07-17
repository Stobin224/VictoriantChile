using System.Security.Cryptography;
using System.Text;
using VictoriantChile.Simulation.Core.State;

namespace VictoriantChile.Simulation.Runner
{
    public sealed class GameStateHasher
    {
        public string ComputeHash(GameState state)
        {
            string compact = new CanonicalGameStateSerializer().ToCompactJson(state);
            byte[] bytes = Encoding.UTF8.GetBytes(compact);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder("sha256:", 71);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
