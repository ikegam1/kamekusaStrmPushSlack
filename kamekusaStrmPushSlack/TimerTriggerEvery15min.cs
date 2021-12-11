//#r "Newtonsoft.Json"

using System;
using System.Text;
using System.Net;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;

namespace kamekusaStrmPushSlack
{
    public static class TimerTriggerEvery15min
    {
        [FunctionName("TimerTriggerEvery15min")]
        public static async Task RunAsync([TimerTrigger("0 */15 * * * *")] TimerInfo myTimer,
            ILogger log)
        {
            const string WEBHOOK_URL = "https://hooks.slack.com/services/xxxxxxxx";
            const string EndpointUri = "https://xxxxxxxx.documents.azure.com:443/";
            const string PrimaryKey = "xxxxxxxx";
            const string databaseId = "xxxxxxxx";
            const string containerId = "iot";

            var wc = new WebClient();
            string slack_text = "[kame-moving] ts:";

            wc.Headers.Add(HttpRequestHeader.ContentType, "application/json;charset=UTF-8");
            wc.Encoding = Encoding.UTF8;

            Int32 unixTimestamp = (Int32)(DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            //cosmosdbのタイムゾーンとズレてたのでとりあえず9時間も追加で引く
            //Int32 before15Min = unixTimestamp - 15 * 60 - (60 * 60 * 9);
            Int32 before15Min = unixTimestamp - 15 * 60; //勘違い？合ってた。
            slack_text += unixTimestamp.ToString();
            slack_text += "\n";

            CosmosClient cosmosClient;
            Microsoft.Azure.Cosmos.Database database;
            Container container;
            cosmosClient = new CosmosClient(EndpointUri, PrimaryKey);
            database = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseId);
            container = await database.CreateContainerIfNotExistsAsync(containerId, "/_partitionKey");

            var sqlQueryText = "SELECT * FROM c WHERE c.label = 'kamekusa' AND c._ts >= " + before15Min.ToString() + " OFFSET 0 LIMIT 1000";

            log.LogInformation("Running query: {sqlQueryText}\n");

            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
            FeedIterator<kamekusaStreamPushSlack.AnalyticsItem> queryResultSetIterator =
                   container.GetItemQueryIterator<kamekusaStreamPushSlack.AnalyticsItem>(queryDefinition);

            List<kamekusaStreamPushSlack.AnalyticsItem> analyticsItems = new List<kamekusaStreamPushSlack.AnalyticsItem>();
            List<float> avgX = new List<float>();
            List<float> avgY = new List<float>();

            const int SAMPLING_UNIT = 10; //連続したポイントの平均値を取る単位
            while (queryResultSetIterator.HasMoreResults)
            {
                Microsoft.Azure.Cosmos.FeedResponse<kamekusaStreamPushSlack.AnalyticsItem> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                float x = 0, y = 0, w = 0, h = 0;
                int i = 0;
                foreach (kamekusaStreamPushSlack.AnalyticsItem analyticsItem in currentResultSet)
                {
                    analyticsItems.Add(analyticsItem);
                    log.LogInformation("\tRead " + analyticsItem.Ts + "");

                    x += analyticsItem.BBox[0];
                    y += analyticsItem.BBox[1];
                    w += analyticsItem.BBox[2];
                    h += analyticsItem.BBox[3];


                    if (i % SAMPLING_UNIT == 0)
                    {
                        avgX.Add((float)((x + w) / 2) / SAMPLING_UNIT);
                        avgY.Add((float)((y + h) / 2) / SAMPLING_UNIT);
                        x = analyticsItem.BBox[0];
                        y = analyticsItem.BBox[1];
                        w = analyticsItem.BBox[2];
                        h = analyticsItem.BBox[3];
                    }

                    i += 1;
                }
            }

            float prevX = 0;
            float prevY = 0;
            float moved = 0;
            for (int i = 0; i < avgX.Count; i++)
            {
                Console.WriteLine("\tMoved {0}\n", Math.Abs((prevX - avgX[i])) + Math.Abs((prevY - avgY[i])));
                if (prevX > 0 && Math.Abs((prevX - avgX[i])) + Math.Abs((prevY - avgY[i])) > 0.05)
                {
                    moved += Math.Abs((prevX - avgX[i])) + Math.Abs((prevY - avgY[i]));
                    log.LogInformation("\tMoved " + moved);
                    log.LogInformation("\tMoved " + prevX);
                    log.LogInformation("\tMoved " + avgX[i]);
                    log.LogInformation("\tMoved " + prevY);
                    log.LogInformation("\tMoved " + avgY[i]);
                    //slack_text = $"{slack_text}moved: {string.Format("{0:F1}", (moved / 0.3))}";
                }
                prevX = avgX[i];
                prevY = avgY[i];
            }

            if (moved >= 0.3)
            {
                slack_text = $"{slack_text}:turtle:が{string.Format("{0:F1}", (moved / 0.3))}歩、歩いたよ！";
                var data = JsonConvert.SerializeObject(new
                {
                    text = slack_text,
                });
                wc.UploadString(WEBHOOK_URL, data);
            }
        }

        private static object GetDistance(float x, float y, float w, float h)
        {
            return (float)Math.Sqrt((Math.Pow(x - w, 2) + Math.Pow(y - h, 2)));
        }
    }
}