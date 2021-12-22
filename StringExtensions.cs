namespace ShinyHunter
{
    public static class StringExtensions
    {
        public static string AlphaNumerics(this string str)
        {
            return new string(str.Where(character => char.IsLetterOrDigit(character)).ToArray());
        }
    }
}