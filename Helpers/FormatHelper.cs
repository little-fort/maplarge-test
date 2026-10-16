namespace TestProject.Helpers
{
    public class FormatHelper
    {
        private const decimal _KB = 1024;
        private const decimal _MB = _KB * 1024;
        private const decimal _GB = _MB * 1024;
        private const decimal _TB = _GB * 1024;
        private const decimal _PB = _TB * 1024;

        public static string FormatDisplaySize(long size)
        {
            if (size < 1000)
                return "0 KB";
            if (size < 1_000_000)
                return $"{Math.Round(size / _KB)} KB";
            if (size < 1_000_000_000)
                return $"{Math.Round(size / _MB)} MB";
            if (size < 1_000_000_000_000)
                return $"{Math.Round(size / _GB)} GB";
            if (size < 1_000_000_000_000_000)
                return $"{Math.Round(size / _TB)} TB";
            
            return $"{Math.Round(size / _PB)} PB";
        }
    }
}
