using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using CommandLine;
using Dapper;
using Microsoft.Data.SqlClient;

namespace HtmlEncodingRemoval
{
    class Options
    {
        [Option('c', Required = true, HelpText = "SQL Server Connection String")]
        public string ConnectionString { get; set; }
    }

    class HtmlEncodedItem
    {
        public Guid Id { get; set; }
        public string HtmlContent { get; set; }
    }

    class Program
    {
        public static Options Options { get; set; }

        static async Task Main(string[] args)
        {
            var parserResult = Parser.Default.ParseArguments<Options>(args);
            if (parserResult.Tag == ParserResultType.Parsed)
            {
                Options = ((Parsed<Options>)parserResult).Value;

                Console.WriteLine("Press any key to start.");
                Console.ReadKey();
                WriteMessage($"Connecting to database.", ConsoleColor.Gray);

                await using var conn = new SqlConnection(Options.ConnectionString);
                WriteMessage($"Connected to database.", ConsoleColor.Gray);

                var pages = (await conn.QueryAsync<HtmlEncodedItem>(
                    "SELECT Id, HtmlContent FROM CustomPage cp")).ToList();
                foreach (var page in pages)
                {
                    var decodedHtml = HttpUtility.HtmlDecode(page.HtmlContent);
                    var sqlPages = "UPDATE CustomPage SET HtmlContent = @decodedHtml WHERE Id = @pageId";
                    await conn.ExecuteAsync(sqlPages, new { decodedHtml, pageId = page.Id });
                }
                WriteMessage($"Custom Pages Decoded, total {pages.Count} item(s).", ConsoleColor.Gray);

                var posts = (await conn.QueryAsync<HtmlEncodedItem>(
                    "SELECT Id, PostContent AS HtmlContent FROM Post p")).ToList();
                foreach (var post in posts)
                {
                    var decodedHtml = HttpUtility.HtmlDecode(post.HtmlContent);
                    var sqlPosts = "UPDATE Post SET PostContent = @decodedHtml WHERE Id = @postId";
                    await conn.ExecuteAsync(sqlPosts, new { decodedHtml, postId = post.Id });
                }
                WriteMessage($"Posts Decoded, total {posts.Count} item(s).", ConsoleColor.Gray);
            }

            WriteMessage($"Done.", ConsoleColor.Green);
            Console.ReadKey();
        }

        static void WriteMessage(string message, ConsoleColor color = ConsoleColor.White, bool resetColor = true)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            if (resetColor)
            {
                Console.ResetColor();
            }
        }
    }
}
