using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Text;
using System.Collections.Generic;

namespace kamekusaStrmPushSlack
{
    public static class HttpExample
    {
        [FunctionName("HttpExample")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            var wc = new WebClient();

            wc.Headers.Add(HttpRequestHeader.ContentType, "application/json;charset=UTF-8");
            wc.Encoding = Encoding.UTF8;

            Int32 unixTimestamp = (Int32)(DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            //cosmosdbのタイムゾーンとズレてたのでとりあえず9時間も追加で引く
            Int32 before7Day = unixTimestamp - 7 * 24 * 60 * 60 - (60 * 60 * 9);

            // The Azure Cosmos DB endpoint for running this sample.
            string EndpointUri = "https://xxxxxxxx.documents.azure.com:443/";
            // The primary key for the Azure Cosmos account.
            string PrimaryKey = "xxxxxxxx"; CosmosClient cosmosClient;
            Microsoft.Azure.Cosmos.Database database;
            Container container;
            string databaseId = "kame-analytics";
            string containerId = "iot";

            cosmosClient = new CosmosClient(EndpointUri, PrimaryKey);
            database = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseId);
            container = await database.CreateContainerIfNotExistsAsync(containerId, "/_partitionKey");

            var sqlQueryText = "SELECT c.id, c.timestamp, c.label, c._ts, c.bbox FROM c ";
            sqlQueryText += "WHERE c._ts >= " + before7Day.ToString() + " OFFSET 0 LIMIT 100000";

            Console.WriteLine("Running query: {0}\n", sqlQueryText);
            log.LogInformation("Running query: {sqlQueryText}\n");
            log.LogInformation(before7Day.ToString());

            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
            FeedIterator<kamekusaStreamPushSlack.AnalyticsItem> queryResultSetIterator =
                   container.GetItemQueryIterator<kamekusaStreamPushSlack.AnalyticsItem>(queryDefinition);

            List<kamekusaStreamPushSlack.AnalyticsItem> analyticsItems = new List<kamekusaStreamPushSlack.AnalyticsItem>();
            List<float> avgX = new List<float>();
            List<float> avgY = new List<float>();
            List<kamekusaStreamPushSlack.ParsedItem> parsedItems = new List<kamekusaStreamPushSlack.ParsedItem>();
            const int SAMPLING_UNIT = 4; //連続したポイントの平均値を取る単位

            while (queryResultSetIterator.HasMoreResults)
            {
                Microsoft.Azure.Cosmos.FeedResponse<kamekusaStreamPushSlack.AnalyticsItem> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                float x = 0, y = 0, w = 0, h = 0;
                int i = 0;
                kamekusaStreamPushSlack.ParsedItem parsed = new kamekusaStreamPushSlack.ParsedItem();
                foreach (kamekusaStreamPushSlack.AnalyticsItem analyticsItem in currentResultSet)
                {
                    analyticsItems.Add(analyticsItem);
                    parsed = new kamekusaStreamPushSlack.ParsedItem();
                    log.LogInformation("\tRead " + analyticsItem.Id + "");

                    x += analyticsItem.BBox[0];
                    y += analyticsItem.BBox[1];
                    w += analyticsItem.BBox[2];
                    h += analyticsItem.BBox[3];
                    Console.WriteLine("BBox0: {0}\n", analyticsItem.BBox[0]);
                    if (i % SAMPLING_UNIT == 0)
                    {
                        avgX.Add((float)((x + w) / 2) / SAMPLING_UNIT);
                        avgY.Add((float)((y + h) / 2) / SAMPLING_UNIT);
                        x = analyticsItem.BBox[0];
                        y = analyticsItem.BBox[1];
                        w = analyticsItem.BBox[2];
                        h = analyticsItem.BBox[3];
                        parsed.X = (float)((x + w) / 2) / SAMPLING_UNIT;
                        parsed.Y = (float)((y + h) / 2) / SAMPLING_UNIT;
                        parsed.Timestamp = analyticsItem.Timestamp;
                        parsedItems.Add(parsed);
                    }

                    i += 1;
                }
            }

            float prevX = 0;
            float prevY = 0;
            float moved = 0;
            for (int i = 0; i < parsedItems.Count; i++)
            {
                Console.WriteLine("\tMoved {0}\n", Math.Abs((prevX - avgX[i])) + Math.Abs((prevY - avgY[i])));
                if (prevX > 0 && Math.Abs((prevX - avgX[i])) + Math.Abs((prevY - avgY[i])) > 0.05)
                {
                    moved = Math.Abs((prevX - avgX[i])) + Math.Abs((prevY - avgY[i]));
                    parsedItems[i].Moved = moved;
                    if (prevX - avgX[i] > 0 && Math.Abs((prevX - avgX[i])) > Math.Abs((prevY - avgY[i])))
                    {
                        //right
                        parsedItems[i].Direction = "R";
                    }
                    else if (prevX - avgX[i] < 0 && Math.Abs((prevX - avgX[i])) > Math.Abs((prevY - avgY[i])))
                    {
                        //left
                        parsedItems[i].Direction = "L";
                    }
                    else if (prevY - avgY[i] < 0 && Math.Abs((prevX - avgX[i])) < Math.Abs((prevY - avgY[i])))
                    {
                        //up
                        parsedItems[i].Direction = "U";
                    }
                    else
                    {
                        //down
                        parsedItems[i].Direction = "D";
                    }
                }
                prevX = avgX[i];
                prevY = avgY[i];
            }

            var response = JsonConvert.SerializeObject(new
            {
                body = parsedItems
            });

            return new ContentResult() { Content = response, ContentType = "application/json" };
        }
    }
}

