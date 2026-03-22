using System.Text;

namespace WindowsHinting
{
    internal static class LabelGenerator
    {
        public static string[] Generate(int n)
        {
            var labels = new string[n];
            for (int i = 0; i < n; i++)
                labels[i] = ToLetters(i);
            return labels;
        }

        private static string ToLetters(int index)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var sb = new StringBuilder();
            int x = index;

            while (true)
            {
                sb.Insert(0, alphabet[x % 26]);
                x = x / 26 - 1;
                if (x < 0) break;
            }

            return sb.ToString();
        }
    }
}
