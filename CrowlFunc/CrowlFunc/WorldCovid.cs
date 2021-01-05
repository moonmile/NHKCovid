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
using System.Globalization;
using System.Linq;

namespace CrowlFunc
{
    public static class WorldCovid
    {
        [FunctionName("WorldCovid")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("called WorldCovid");
            var url = "https://raw.githubusercontent.com/CSSEGISandData/COVID-19/master/csse_covid_19_data/csse_covid_19_time_series/time_series_covid19_confirmed_global.csv";
            // CSV�`���Ŏ擾
            try
            {
                var cl = new HttpClient();
                // 1�s���ǂݍ��� JSON �`���ɕϊ�
                var res = await cl.GetAsync(url);
                var dates = new List<DateTime>();
                var data = new List<Covid>();


                using (var st = new StreamReader(await res.Content.ReadAsStreamAsync()))
                {
                    // 1�s�ڂ�����t���擾
                    //  5�J�����ڂ�����t
                    // �����A�����͂Ȃ����O�̂���
                    string line = st.ReadLine();
                    var items = line.Split(",");
                    var culture = new CultureInfo("en-US");
                    for (int i = 4; i < items.Length; i++)
                    {
                        dates.Add(DateTime.Parse(items[i], culture));
                    }

                    // 2�s�ڂ��獑���Ɨz���Ґ�
                    //  1�J�����ڂ���, 2�J�����ڂɍ���, 5�J�����ڂ���z���Ґ�
                    while (true)
                    {
                        line = st.ReadLine();
                        if (string.IsNullOrEmpty(line)) break;
                        // ������ϊ����Ă����B�J���}�����ɂ���
                        // "Korea, South"
                        // "Bonaire, Sint Eustatius and Saba"
                        line = line.Replace("\"Korea, South\"", "Korea South");
                        line = line.Replace("\"Bonaire, Sint Eustatius and Saba\"", "Bonaire Sint Eustatius and Saba");
                        items = line.Split(",");
                        if (items.Length >= dates.Count + 4)
                        {
                            if (items[0] != "") continue;
                            int pre = 0;
                            string location = items[1];

                            for ( int i=5; i<items.Length; i++ )
                            {
                                try
                                {
                                    var date = dates[i - 4];
                                    int cases = int.Parse(items[i]) - pre;
                                    pre = int.Parse(items[i]);
                                    var it = new Covid()
                                    {
                                        Date = date,
                                        Location = location,
                                        Cases = cases,
                                    };
                                    data.Add(it);
                                } 
                                catch ( Exception ex )
                                {
                                    log.LogError(ex.Message);
                                }
                            }
                        }
                    }
                    // �\�[�g���Ă���
                    data = data.OrderBy(t => t.Location).ThenBy(t => t.Date).ToList();
                    // �T���ς��v�Z
                    NHKCovid.calcCasesAve(data);
                    // �T�P��Rt�l���v�Z
                    NHKCovid.calcCasesRt(data);
                    // �T�P��Rt���ϒl���v�Z
                    NHKCovid.calcCasesRtAve(data);

                    return new OkObjectResult(new { result = data });
                }
            } 
            catch ( Exception ex )
            {
                log.LogError( ex.Message);
                return new NotFoundResult();
            }
        }

        [FunctionName("WorldCovidTimerOne")]
        public static async Task<IActionResult> RunTimerOne(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Blob("covid/world.json", FileAccess.Write)] Stream jsonfile,
            ILogger log)
        {
            await WorldCovid.RunTimer(null, jsonfile, log);
            return new OkObjectResult("world.json " + DateTime.Now.ToString());
        }

        [FunctionName("WorldCovidTimer")]
        public static async Task RunTimer([TimerTrigger("0 5 * * * *")] TimerInfo myTimer,
            [Blob("covid/world.json", FileAccess.Write)] Stream jsonfile,
            ILogger log)

        {
            log.LogInformation("called WorldCovidTimer");
            var url = "https://raw.githubusercontent.com/CSSEGISandData/COVID-19/master/csse_covid_19_data/csse_covid_19_time_series/time_series_covid19_confirmed_global.csv";

            var cl = new HttpClient();
            // 1�s���ǂݍ��� JSON �`���ɕϊ�
            var res = await cl.GetAsync(url);
            var dates = new List<DateTime>();
            var data = new List<Covid>();


            using (var st = new StreamReader(await res.Content.ReadAsStreamAsync()))
            {
                // 1�s�ڂ�����t���擾
                //  5�J�����ڂ�����t
                // �����A�����͂Ȃ����O�̂���
                string line = st.ReadLine();
                var items = line.Split(",");
                var culture = new CultureInfo("en-US");
                for (int i = 4; i < items.Length; i++)
                {
                    dates.Add(DateTime.Parse(items[i], culture));
                }

                // 2�s�ڂ��獑���Ɨz���Ґ�
                //  1�J�����ڂ���, 2�J�����ڂɍ���, 5�J�����ڂ���z���Ґ�
                while (true)
                {
                    line = st.ReadLine();
                    if (string.IsNullOrEmpty(line)) break;
                    // ������ϊ����Ă����B�J���}�����ɂ���
                    // "Korea, South"
                    // "Bonaire, Sint Eustatius and Saba"
                    line = line.Replace("\"Korea, South\"", "Korea South");
                    line = line.Replace("\"Bonaire, Sint Eustatius and Saba\"", "Bonaire Sint Eustatius and Saba");
                    items = line.Split(",");
                    if (items.Length >= dates.Count + 4)
                    {
                        if (items[0] != "") continue;
                        int pre = 0;
                        string location = items[1];

                        for (int i = 5; i < items.Length; i++)
                        {
                            try
                            {
                                var date = dates[i - 4];
                                int cases = int.Parse(items[i]) - pre;
                                pre = int.Parse(items[i]);
                                var it = new Covid()
                                {
                                    Date = date,
                                    Location = location,
                                    Cases = cases,
                                };
                                data.Add(it);
                            }
                            catch (Exception ex)
                            {
                                log.LogError(ex.Message);
                            }
                        }
                    }
                }
                // �\�[�g���Ă���
                data = data.OrderBy(t => t.Location).ThenBy(t => t.Date).ToList();
                // �T���ς��v�Z
                NHKCovid.calcCasesAve(data);
                // �T�P��Rt�l���v�Z
                NHKCovid.calcCasesRt(data);
                // �T�P��Rt���ϒl���v�Z
                NHKCovid.calcCasesRtAve(data);
            }
            var json = JsonConvert.SerializeObject(new { result = data });
            var writer = new StreamWriter(jsonfile);
            writer.Write(json);
            writer.Close();
            // return new OkObjectResult("save json " + DateTime.Now.ToString());
        }

    }
}
