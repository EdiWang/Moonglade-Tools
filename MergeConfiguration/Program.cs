using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Dapper;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace MergeConfiguration
{
    class Options
    {
        [Option('c', Required = true, HelpText = "SQL Server Connection String")]
        public string ConnectionString { get; set; }
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

                await using (var conn = new SqlConnection(Options.ConnectionString))
                {
                    WriteMessage($"Connected to database.", ConsoleColor.Gray);

                    // 1. Must set COMPATIBILITY_LEVEL = 130 to enable OPENJSON function
                    // Reference: https://docs.microsoft.com/en-us/sql/t-sql/functions/openjson-transact-sql?view=sql-server-ver15
                    var sql1 = @$"ALTER DATABASE [{conn.Database}] SET COMPATIBILITY_LEVEL = 130";
                    await conn.ExecuteAsync(sql1);

                    // 2. Merge old BlogOwnerSettings json object into GeneralSettings json object
                    var sql2 = @"DECLARE @jsonFrom NVARCHAR(MAX), @jsonTo NVARCHAR(MAX)
                                 SELECT @jsonFrom = bc.CfgValue FROM BlogConfiguration bc WHERE CfgKey = 'BlogOwnerSettings'
                                 SELECT @jsonTo = bc.CfgValue FROM BlogConfiguration bc WHERE CfgKey = 'GeneralSettings'
                                 
                                 SELECT [key], [value]
                                 FROM OPENJSON(@jsonFrom)
                                 UNION ALL
                                 SELECT [key], [value]
                                 FROM OPENJSON(@jsonTo)";
                    var kvp = await conn.QueryAsync<KeyValuePair<string, string>>(sql2);
                    var dic = kvp.ToDictionary(c => c.Key, c => c.Value);
                    if (dic.Any())
                    {
                        var gs = new GeneralSettings
                        {
                            AvatarBase64 = dic[nameof(GeneralSettings.AvatarBase64)],
                            Copyright = dic[nameof(GeneralSettings.Copyright)],
                            Description = dic[nameof(GeneralSettings.Description)],
                            FooterCustomizedHtmlPitch = dic[nameof(GeneralSettings.FooterCustomizedHtmlPitch)],
                            LogoText = dic[nameof(GeneralSettings.LogoText)],
                            MetaDescription = dic[nameof(GeneralSettings.MetaDescription)],
                            SiteTitle = dic[nameof(GeneralSettings.SiteTitle)],
                            MetaKeyword = dic[nameof(GeneralSettings.MetaKeyword)],
                            OwnerName = dic["Name"], // Special one :)
                            ShortDescription = dic[nameof(GeneralSettings.ShortDescription)],
                            SideBarCustomizedHtmlPitch = dic[nameof(GeneralSettings.SideBarCustomizedHtmlPitch)],
                            SiteIconBase64 = dic[nameof(GeneralSettings.SiteIconBase64)],
                            ThemeFileName = dic[nameof(GeneralSettings.ThemeFileName)],
                            TimeZoneId = dic[nameof(GeneralSettings.TimeZoneId)],
                            TimeZoneUtcOffset = dic[nameof(GeneralSettings.TimeZoneUtcOffset)]
                        };

                        // Can not use System.Text.Json due to non-english char support
                        var mergedJson = JsonConvert.SerializeObject(gs);

                        var sql3 = "UPDATE BlogConfiguration SET CfgValue = @CfgValue WHERE CfgKey = 'GeneralSettings'";
                        await conn.ExecuteAsync(sql3, new { CfgValue = mergedJson });

                        // 3. Delete BlogOwnerSettings row
                        // comment out for backward compability
                        // var sqlDeleteRow = "DELETE FROM BlogConfiguration WHERE CfgKey = 'BlogOwnerSettings'";
                        // await conn.ExecuteAsync(sqlDeleteRow);
                    }
                }

                WriteMessage($"Done.", ConsoleColor.Green);
                Console.ReadKey();
            }
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

        private class ConfigurationJsonModel
        {
            public string Key { get; set; }

            public string Value { get; set; }
        }
    }

    public class GeneralSettings
    {
        public string SiteTitle { get; set; }

        public string LogoText { get; set; }

        public string MetaKeyword { get; set; }

        public string MetaDescription { get; set; }

        public string Copyright { get; set; }

        public string SideBarCustomizedHtmlPitch { get; set; }

        public string FooterCustomizedHtmlPitch { get; set; }

        public string TimeZoneUtcOffset { get; set; }

        public string TimeZoneId { get; set; }

        public string ThemeFileName { get; set; }

        public string SiteIconBase64 { get; set; }

        public string OwnerName { get; set; }

        public string Description { get; set; }

        public string ShortDescription { get; set; }

        public string AvatarBase64 { get; set; }

        public GeneralSettings()
        {
            ThemeFileName = "word-blue.css";
            SiteIconBase64 = string.Empty;
            AvatarBase64 = string.Empty;
        }
    }
}
