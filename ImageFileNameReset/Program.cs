using System;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Dapper;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using NLog;
using NLog.Config;

namespace ImageFileNameReset
{
    class Options
    {
        [Option('c', Required = true, HelpText = "Azure Blob Storage Connection String")]
        public string AzBlobConnectionString { get; set; }

        [Option('s', Required = true, HelpText = "SQL Server Connection String")]
        public string SqlSeverConnectionString { get; set; }

        [Option('n', Required = true, HelpText = "Container Name")]
        public string AzBlobContainerName { get; set; }
    }

    class BlogPostInfo
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
    }

    class Program
    {
        public static Options Options { get; set; }

        private static Logger _logger;

        static async Task Main(string[] args)
        {
            LogManager.Configuration = new XmlLoggingConfiguration($@"{AppContext.BaseDirectory}\nlog.config");
            _logger = LogManager.GetCurrentClassLogger();

            var parserResult = Parser.Default.ParseArguments<Options>(args);
            if (parserResult.Tag == ParserResultType.Parsed)
            {
                Options = ((Parsed<Options>)parserResult).Value;
                WriteMessage("Connecting to Azure...");

                var storageAccount = CloudStorageAccount.Parse(Options.AzBlobConnectionString);
                var blobClient = storageAccount.CreateCloudBlobClient();
                var container = blobClient.GetContainerReference(Options.AzBlobContainerName);

                WriteMessage($"Using Azure Blob Storage '{storageAccount.Credentials.AccountName}' with container '{Options.AzBlobContainerName}'.", ConsoleColor.Green);

                WriteMessage("Getting image file list...");
                var blobs = await container.ListBlobsSegmentedAsync(null);
                var blobImages = (from item in blobs.Results
                                  where item.GetType() == typeof(CloudBlockBlob)
                                  select (CloudBlockBlob)item
                                  into blob
                                  select new BlobImage(blob.Properties.LastModified, blob.Uri)
                                  {
                                      FileName = blob.Name
                                  })
                    .OrderByDescending(p => p.LastModified)
                    .ToList();

                WriteMessage($"Found {blobImages.Count} image file(s).", ConsoleColor.Yellow);

                _logger.Info($"Found {blobImages.Count} image file(s) on Azure Blob Storage '{storageAccount.Credentials.AccountName}' with container '{Options.AzBlobContainerName}'");

                var imagesToBeRenamed = blobImages.Where(b => !b.FileName.StartsWith("img-")).ToList();
                if (imagesToBeRenamed.Any())
                {
                    WriteMessage($"Found {imagesToBeRenamed.Count} image file(s) with non-standard names.", ConsoleColor.Yellow);
                    _logger.Info($"Found {imagesToBeRenamed.Count} image file(s) with non-standard names.");

                    using (var conn = new SqlConnection(Options.SqlSeverConnectionString))
                    {
                        int itemsRenamed = 0;

                        foreach (var img in imagesToBeRenamed)
                        {
                            const string sqlFindInfo = @"SELECT TOP 1 Id, Title FROM Post p
                                                         WHERE p.PostContent LIKE '%' + @oldFileName + '%'";

                            var pi = await conn.QueryFirstOrDefaultAsync<BlogPostInfo>(sqlFindInfo, new { oldFileName = img.FileName });
                            if (null == pi)
                            {
                                WriteMessage(
                                    $"Image file '{img.FileName}' seems not used in any post. Please consider delete it.");
                            }
                            else
                            {
                                WriteMessage($"Found refrencing post '{pi.Title}' (Id: {pi.Id})");

                                var gen = new GuidFileNameGenerator(Guid.NewGuid());
                                var newFileName = gen.GetFileName(img.FileName);

                                WriteMessage($"Renaming {img.FileName} to {newFileName}.");
                                _logger.Info(
                                    $"Renaming {img.FileName} to {newFileName} in refrencing post '{pi.Title}' with Id: '{pi.Id}'");

                                try
                                {
                                    // 1. Update Database
                                    const string sqlUpdate = @"UPDATE Post
                                                               SET PostContent = REPLACE(PostContent, @oldFileName, @newFileName)
                                                               WHERE Id = @postId";
                                    int rows = await conn.ExecuteAsync(sqlUpdate,
                                        new
                                        {
                                            oldFileName = "/" + img.FileName,
                                            newFileName = "/" + newFileName,
                                            postId = pi.Id
                                        });

                                    if (rows > 0)
                                    {
                                        try
                                        {
                                            // 2. Update Blob only SQL operation is successful.
                                            await RenameAsync(container, img.FileName, newFileName);
                                            itemsRenamed++;

                                            _logger.Info($"Renamed {img.FileName} to {newFileName}.");
                                        }
                                        catch (Exception e)
                                        {
                                            WriteErrorMessage(e.Message);

                                            // Roll back SQL changes
                                            WriteMessage("Azure Blob renaming blow up, roll back database changes.",
                                                ConsoleColor.DarkYellow);
                                            int rollbackRows = await conn.ExecuteAsync(sqlUpdate, new
                                            {
                                                oldFileName = newFileName,
                                                newFileName = img.FileName,
                                                postId = pi.Id
                                            });
                                            WriteMessage($"{rollbackRows} row(s) updated.");

                                            _logger.Warn(
                                                $"Rollback renaming for {img.FileName} to {newFileName}. {rollbackRows} row(s) updated.");
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    WriteErrorMessage(e.Message);
                                }
                            }
                        }

                        WriteMessage($"{itemsRenamed} image file(s) updated. {blobImages.Count - itemsRenamed} skipped.");
                    }
                }

                Console.ReadKey();
            }
        }

        private static async Task RenameAsync(CloudBlobContainer container, string oldName, string newName)
        {
            try
            {
                var source = (CloudBlockBlob)await container.GetBlobReferenceFromServerAsync(oldName);
                var target = container.GetBlockBlobReference(newName);

                await target.StartCopyAsync(source);

                while (target.CopyState.Status == CopyStatus.Pending)
                    await Task.Delay(100);

                if (target.CopyState.Status != CopyStatus.Success)
                    throw new Exception("Rename failed: " + target.CopyState.Status);

                await source.DeleteAsync();
            }
            catch (Exception e)
            {
                WriteErrorMessage(e.Message);
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
    }
}
