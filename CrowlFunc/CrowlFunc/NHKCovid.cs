using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace CrowlFunc
{
    public static class NHKCovid
    {
        [FunctionName("NHKCovidRead")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("called NHKCovid");

            var url = "https://www3.nhk.or.jp/n-data/opendata/coronavirus/nhk_news_covid19_prefectures_daily_data.csv";
            // CSV形式で取得
            try
            {
                var cl = new HttpClient();
                // 1行ずつ読み込み JSON 形式に変換
                var res = await cl.GetAsync(url);
                using (var st = new StreamReader(await res.Content.ReadAsStreamAsync()))
                {
                    // タイトルは読み飛ばし
                    st.ReadLine();
                    var data = new List<Covid>();
                    while (true)
                    {
                        string line = st.ReadLine();
                        if (string.IsNullOrEmpty(line)) break;
                        var items = line.Split(",");
                        if (items.Length >= 7)
                        {
                            var it = new Covid()
                            {
                                Date = DateTime.Parse(items[0]),
                                LocationId = int.Parse(items[1]),
                                Location = items[2],
                                Cases = int.Parse(items[3]),
                                CasesTotal = int.Parse(items[3]),
                                Deaths = int.Parse(items[3]),
                                DeathsTotal = int.Parse(items[3]),
                            };
                            data.Add(it);
                        }
                    }
                    // ソートしておく
                    data = data.OrderBy(t => t.LocationId).ThenBy(t => t.Date).ToList();
                    // 週平均を計算
                    calcCasesAve(data);
                    // 週単位Rt値を計算
                    calcCasesRt(data);
                    // 週単位Rt平均値を計算
                    calcCasesRtAve(data);

                    return new OkObjectResult(new { result = data });
                }
            } 
            catch
            {
                return new NotFoundResult();
            }
        }

        [FunctionName("NHKCovid")]
        public static async Task<IActionResult> RunRead(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Blob("covid/japan.json", FileAccess.Read)] Stream jsonfile,
            ILogger log)
        {
            log.LogInformation("called NHKCovid");
            var sr = new StreamReader(jsonfile);
            var json = await sr.ReadToEndAsync();
            return new OkObjectResult(json);
        }

        [FunctionName("NHKCovidTimerOne")]
        public static async Task<IActionResult> RunTimerOne(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Blob("covid/japan.json", FileAccess.Write)] Stream jsonfile,
            ILogger log)
        {
            await NHKCovid.RunTimer(null, jsonfile, log);
            return new OkObjectResult("japan.json " + DateTime.Now.ToString());
        }

        /// <summary>
        /// ワーク領域から落ちないように 5分間隔で起動しておく
        /// </summary>
        /// <param name="myTimer"></param>
        /// <param name="jsonfile"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("Heartbeat")]
        public static void Heartbeat([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
            [Blob("covid/japan.json", FileAccess.Write)] Stream jsonfile,
            ILogger log)

        {
            log.LogInformation("called Heartbeat");
        }

        [FunctionName("NHKCovidTimer")]
        public static async Task RunTimer([TimerTrigger("0 5 * * * *")] TimerInfo myTimer,
            [Blob("covid/japan.json", FileAccess.Write)] Stream jsonfile,
            ILogger log)

        {
            log.LogInformation("called NHKCovidTimer");
            var url = "https://www3.nhk.or.jp/n-data/opendata/coronavirus/nhk_news_covid19_prefectures_daily_data.csv";
            var cl = new HttpClient();
            // 1行ずつ読み込み JSON 形式に変換
            var res = await cl.GetAsync(url);
            var data = new List<Covid>();
            using (var st = new StreamReader(await res.Content.ReadAsStreamAsync()))
            {
                // タイトルは読み飛ばし
                st.ReadLine();
                while (true)
                {
                    string line = st.ReadLine();
                    if (string.IsNullOrEmpty(line)) break;
                    var items = line.Split(",");
                    if (items.Length >= 7)
                    {
                        var it = new Covid()
                        {
                            Date = DateTime.Parse(items[0]),
                            LocationId = int.Parse(items[1]),
                            Location = items[2],
                            Cases = int.Parse(items[3]),
                            CasesTotal = int.Parse(items[3]),
                            Deaths = int.Parse(items[3]),
                            DeathsTotal = int.Parse(items[3]),
                        };
                        data.Add(it);
                    }
                }
                // ソートしておく
                data = data.OrderBy(t => t.LocationId).ThenBy(t => t.Date).ToList();
                // 週平均を計算
                calcCasesAve(data);
                // 週単位Rt値を計算
                calcCasesRt(data);
                // 週単位Rt平均値を計算
                calcCasesRtAve(data);
            }
            var json = JsonConvert.SerializeObject(new { result = data });
            var writer = new StreamWriter(jsonfile);
            writer.Write(json);
            writer.Close();
            // return new OkObjectResult("save json " + DateTime.Now.ToString());
        }


        /// <summary>
        /// 週平均を計算
        /// </summary>
        /// <param name="items"></param>
        public static void calcCasesAve( List<Covid> data )
        {
#if false
            foreach ( var it in data )
            {
                int ave = data.Where(t => t.Location == it.Location)
                    .Where(t => it.Date.AddDays(-7) < t.Date && t.Date <= it.Date)
                    .Sum(t => t.Cases) / 7;
                it.CasesAverage = ave;
            }
#else
            // 高速化しておく
            for (int i = 0; i < data.Count(); i++ )
            {
                if (i < 7) continue;
                // 1週間分遡る
                if (data[i - 1].Location != data[i].Location) continue;
                if (data[i - 2].Location != data[i].Location) continue;
                if (data[i - 3].Location != data[i].Location) continue;
                if (data[i - 4].Location != data[i].Location) continue;
                if (data[i - 5].Location != data[i].Location) continue;
                if (data[i - 6].Location != data[i].Location) continue;
                data[i].CasesAverage =
                    (data[i].Cases +
                    data[i - 1].Cases +
                    data[i - 2].Cases +
                    data[i - 3].Cases +
                    data[i - 4].Cases +
                    data[i - 5].Cases +
                    data[i - 6].Cases) / 7;
            }
#endif

        }
        /// <summary>
        /// 週Rtを計算
        /// </summary>
        /// <param name="items"></param>
        public static void calcCasesRt(List<Covid> data)
        {
#if false
            var locations = data.Select(t => t.Location).Distinct();
            foreach (var lo in locations)
            {
                var items = data.Where(t => t.Location == lo).OrderBy(t => t.Date);
                var lastdate = items.LastOrDefault();
                foreach (var it in items)
                {
                    if (it.Date <= lastdate.Date.AddDays(-7))
                    {
                        var dt = it.Date;
                        if (it.Cases > 0 ) {
                            it.CasesRt = ((float)items.Where(t => dt <= t.Date && t.Date < dt.AddDays(7)).Sum(t => t.Cases) / 7.0f) / (float)it.Cases;
                        }
                    }
                }
            }
#else
            // 高速化しておく
            for (int i = 0; i < data.Count()-7; i++)
            {
                var item0 = data[i];
                // 1週間後まで
                if (data[i + 1].Location != data[i].Location) continue;
                if (data[i + 2].Location != data[i].Location) continue;
                if (data[i + 3].Location != data[i].Location) continue;
                if (data[i + 4].Location != data[i].Location) continue;
                if (data[i + 5].Location != data[i].Location) continue;
                if (data[i + 6].Location != data[i].Location) continue;
                if (data[i + 7].Location != data[i].Location) continue;
                if (data[i].Cases == 0) 
                    data[i].CasesRt = 0.0f;
                else 
                    data[i].CasesRt =
                        (float)(data[i+1].Cases +
                        data[i + 2].Cases +
                        data[i + 3].Cases +
                        data[i + 4].Cases +
                        data[i + 5].Cases +
                        data[i + 6].Cases +
                        data[i + 7].Cases) / 7.0f / (float)data[i].Cases;
            }

#endif
        }
        /// <summary>
        /// Rt週平均を計算
        /// </summary>
        /// <param name="items"></param>
        public static void calcCasesRtAve(List<Covid> data)
        {
#if false
            var locations = data.Select(t => t.Location).Distinct();
            foreach (var lo in locations)
            {
                var items = data.Where(t => t.Location == lo).OrderBy(t => t.Date);
                var lastdate = items.LastOrDefault();
                foreach (var it in items)
                {
                    if ( it.Date <= lastdate.Date.AddDays(-14))
                    {
                        var dt = it.Date;
                        it.CasesRtAverage = items.Where(t => dt.AddDays(-7) < t.Date && t.Date <= dt).Sum(t => t.CasesRt) / 7.0f;
                    }
                }
            }
#else
            // 高速化しておく
            for (int i = 0; i < data.Count() - 7; i++)
            {
                var item0 = data[i];
                // 1週間後まで
                if (data[i + 1].Location != data[i].Location) continue;
                if (data[i + 2].Location != data[i].Location) continue;
                if (data[i + 3].Location != data[i].Location) continue;
                if (data[i + 4].Location != data[i].Location) continue;
                if (data[i + 5].Location != data[i].Location) continue;
                if (data[i + 6].Location != data[i].Location) continue;
                if (data[i + 7].Location != data[i].Location) continue;
                if (data[i].CasesAverage == 0)
                    data[i].CasesRtAverage = 0.0f;
                else
                    data[i].CasesRtAverage =
                        (float)(data[i + 1].CasesAverage +
                        data[i + 2].CasesAverage +
                        data[i + 3].CasesAverage +
                        data[i + 4].CasesAverage +
                        data[i + 5].CasesAverage +
                        data[i + 6].CasesAverage +
                        data[i + 7].CasesAverage) / 7.0f / (float)data[i].CasesAverage;
            }

#endif

        }

    }
    public class Covid
    {
        [JsonProperty("date")]
        public DateTime Date { get; set; }
        [JsonProperty("locationId")]
        public int LocationId { get; set; }
        [JsonProperty("location")]
        public string Location { get; set; }
        [JsonProperty("cases")]
        public int Cases { get; set; }
        [JsonProperty("casesTotal")]
        public int CasesTotal { get; set; }
        [JsonProperty("deaths")]
        public int Deaths { get; set; }
        [JsonProperty("deathsTotal")]
        public int DeathsTotal { get; set; }

        [JsonProperty("casesAverage")]
        public float CasesAverage { get; set; }   // 週移動平均
        [JsonProperty("casesRt")]
        public float CasesRt { get; set; }        // 週単位Rt値 = 続く1週間の感染者数平均 / 当日感染者数  
        [JsonProperty("casesRtAverage")]
        public float CasesRtAverage { get; set; }     // Rt値の週移動平均
    }
}
