using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using CommandLine;
using Dapper;
using NLog;
using NLog.Config;

namespace ImageUrlCheck
{
    class Options
    {
        [Option('c', Required = true, HelpText = "SQL Server Connection String")]
        public string SqlSeverConnectionString { get; set; }
    }

    class BlogPostInfo
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string PostContent { get; set; }
    }

    class PostImageInfo
    {
        public BlogPostInfo BlogPostInfo { get; set; }
        public List<string> BadImageUrls { get; set; }

        public PostImageInfo()
        {
            BadImageUrls = new List<string>();
        }
    }

    class Program
    {
        public static Options Options { get; set; }

        private static Logger _logger;

        private static HttpClient _httpClient;

        static async Task Main(string[] args)
        {
            LogManager.Configuration = new XmlLoggingConfiguration($@"{AppContext.BaseDirectory}\nlog.config");
            _logger = LogManager.GetCurrentClassLogger();

            var parserResult = Parser.Default.ParseArguments<Options>(args);
            if (parserResult.Tag == ParserResultType.Parsed)
            {
                Options = ((Parsed<Options>)parserResult).Value;

                // emmm.. bad memory assignment
                var result = new List<PostImageInfo>();
                int totalImageCount = 0;

                // Watch out for performace issue, if data is huge, consider paging.
                var sqlGetAllPostsInfo = "SELECT p.Id, p.Title, p.PostContent FROM Post p";
                using (var conn = new SqlConnection(Options.SqlSeverConnectionString))
                {
                    WriteMessage("Getting posts info.");

                    var posts = await conn.QueryAsync<BlogPostInfo>(sqlGetAllPostsInfo);
                    var blogPostInfos = posts as BlogPostInfo[] ?? posts.ToArray();
                    if (blogPostInfos.Any())
                    {
                        WriteMessage($"Found {blogPostInfos.Length} post(s).", ConsoleColor.Yellow);

                        // emmm.. bad .NET Core practice, never mind, just a one time tool!
                        _httpClient = new HttpClient
                        {
                            BaseAddress = new Uri("https://ediwangstorage.blob.core.windows.net")
                        };
                        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Moonglade-Tools/ImageUrlCheck");

                        foreach (var blogPostInfo in blogPostInfos)
                        {
                            // emmm.. bad memory assignment again, never mind, just a one time tool again!
                            var pii = new PostImageInfo { BlogPostInfo = blogPostInfo };

                            WriteMessage($"Checking post '{blogPostInfo.Title}'");
                            var images = FindImageSrc(blogPostInfo.PostContent);
                            if (images.Any())
                            {
                                totalImageCount += images.Count();
                                foreach (var image in images)
                                {
                                    // bad performance, consider do it in parallel for huge data. anyway it's a one time tool agian, ignore this, hahaha.
                                    if (!await IsImageOk(image))
                                    {
                                        WriteMessage($"Found bad image '{image}'");
                                        pii.BadImageUrls.Add(image);
                                    }
                                }

                                if (pii.BadImageUrls.Any())
                                {
                                    result.Add(pii);
                                }
                            }
                        }
                    }
                }

                if (result.Any())
                {
                    WriteMessage("-----------------------------------------------------------------------", ConsoleColor.Gray);
                    var badImagesCount = result.Sum(r => r.BadImageUrls.Count);
                    WriteMessage($"Scan complete, {badImagesCount} / {totalImageCount} ({((double)badImagesCount / totalImageCount):0.0%}) image(s) are broken.", ConsoleColor.Yellow);

                    foreach (var postImageInfo in result)
                    {
                        foreach (var imageUrl in postImageInfo.BadImageUrls)
                        {
                            _logger.Info($"{postImageInfo.BlogPostInfo.Id}|{postImageInfo.BlogPostInfo.Title}|{imageUrl}");
                        }
                    }
                }

                Console.ReadKey();
            }
        }

        static void WriteErrorMessage(string message)
        {
            _logger.Error(message);
            WriteMessage(message, ConsoleColor.Red);
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

        public static async Task<bool> IsImageOk(string imageUrl)
        {
            if (null != _httpClient)
            {
                imageUrl = imageUrl.Replace("/Uploads/", string.Empty, StringComparison.OrdinalIgnoreCase);
                var response = await _httpClient.GetAsync("ediwang-images/" + imageUrl);
                return response.IsSuccessStatusCode;
            }
            return false;
        }

        public static IEnumerable<string> FindImageSrc(string rawHtmlContent)
        {
            if (string.IsNullOrWhiteSpace(rawHtmlContent)) return null;

            var imgSrcRegex = new Regex("<img.+?src=[\"'](.+?)[\"'].+?>");
            var col = imgSrcRegex.Matches(HttpUtility.HtmlDecode(rawHtmlContent)).Select(m => m.Groups[1].Value);
            return col;
        }
    }
}
