using System;

namespace RsyncAskPass
{
    internal class Program
    {
        static int Main(string[] args)
        {
            var password = Environment.GetEnvironmentVariable("RSYNC_PASSWORD");
            if (!string.IsNullOrEmpty(password))
            {
                Console.WriteLine(password);
                return 0;
            }
            return 1;
        }
    }
}
