using System;
using CommandLine;

namespace HtmlEncodingRemoval
{
    class Options
    {
        [Option('c', Required = true, HelpText = "SQL Server Connection String")]
        public string ConnectionString { get; set; }
    }

    class Program
    {
        public static Options Options { get; set; }

        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }
    }
}
