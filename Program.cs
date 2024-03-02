using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using System.Net.Sockets;

namespace dnspod_ddns
{
    public class StatusRecord
    {
        public Status status;
        public Record record;
    }
    public class StatusRecords
    {
        public Status status;
        public Record[] records;
    }
    public class Status
    {
        public int code;
        public string message;
        public DateTime created_at;
    }
    public class Record
    {
        public int id;
        public string name;
        public string line;
        public string line_id;
        public string type;
        public int ttl;
        public string value;
        public int? weight;
        public int mx;
        public int enabled;
        public string status;
            //"monitor_status": "",
            //"remark": "",
            //"updated_on": "2018-06-11 10:12:51",
            //"use_aqb": "no"
    }

    internal class Program
    {
        private static HttpClient _HttpClient = new HttpClient(); 
        private static void Main(string[] args)
        {
            // dnspod_ddns -t|--token <token> -d|--domain <domain> -s|--subdomain <subdomain> 
            // [-?|-h|--help]
            CommandLineApplication commandLineApplication =
              new CommandLineApplication(throwOnUnexpectedArg: false);
            //CommandArgument names = null;
            //commandLineApplication.Command("name",
            //  (target) =>
            //    names = target.Argument(
            //      "fullname",
            //      "Enter the full name of the person to be greeted.",
            //      multipleValues: true));
            CommandOption token = commandLineApplication.Option(
              "-t | --token <token>",
              "令牌，格式：数字,密匙",
              CommandOptionType.SingleValue);
            CommandOption domain = commandLineApplication.Option(
              "-d | --domain <domain>",
              "域名(例如：youname.com)",
              CommandOptionType.SingleValue);
            //CommandOption subdomain = commandLineApplication.Option(
            //  "-s | --subdomain <subdomain>", "子域名(例如：www)",
            //  CommandOptionType.SingleValue);

            commandLineApplication.HelpOption("-? | -h | --help");
            commandLineApplication.OnExecute(async () =>
            {
                if (!token.HasValue() || !domain.HasValue())
                {
                    commandLineApplication.ShowHelp();
                    return 0;
                }
                //await main(token.Value(), domain.Value(), subdomain.HasValue() ? subdomain.Value() : "@");
                await main(token.Value(), domain.Value(), "@", "diskstation");
                await main(token.Value(), domain.Value(), "pc", "");
                return 0;
            });
            commandLineApplication.Execute(args);
        }

        private static async Task main(string token, string domain, string subdomain, string local_host_name)
        {
            string ip;
            try
            {
                //ip = await getPublicIp();
                //Console.WriteLine($"拿公网ip成功:{ip}");
                ip = GetIPv6Address(local_host_name);
                Console.WriteLine($"拿{local_host_name}的ip成功:{ip}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                //Console.WriteLine($"拿公网ip出错:{ex.Message}");
                Console.WriteLine($"拿{local_host_name}的ip出错:{ex.Message}");
                Console.ResetColor();
                return;
            }

            string last_success_ip = string.Empty;
            string file = Path.GetTempPath() + $"last_success_{local_host_name}_ip.txt";
            try
            {
                if(File.Exists(file))
                    last_success_ip = await File.ReadAllTextAsync(file);
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"读文件{file}出错:{ex.Message}");
                Console.ResetColor();
                return;
            }

            if(last_success_ip == ip)
            {
                Console.WriteLine("ip还是和上次成功提交的一样，无需更新");
                return;
            }

            try
            {
                var record = await query_record(token, domain, subdomain);
                await update_record(token, domain, record, ip);
                Console.WriteLine($"更新dns记录{subdomain}.{domain}成功");
                await File.WriteAllTextAsync(file, ip, System.Text.Encoding.UTF8);
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        }

        private static string GetIPv6Address(string host)
        {
            // 获取本地计算机的主机名
            if(string.IsNullOrEmpty(host))
                host = Dns.GetHostName();

            // 获取主机的IP地址列表
            IPAddress[] addresses = Dns.GetHostAddresses(host);

            foreach (IPAddress address in addresses)
            {
                // 检查是否为IPv6地址
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // 检查是否为本地链路本地地址（Local Link）
                    if (!address.IsIPv6LinkLocal)
                    {
                        return address.ToString();
                    }
                }
            }

            throw new Exception("无ipv6地址，或未开机或未联网");
        }
        private static async Task<string> getPublicIp()
        {
            IPHostEntry hostEntry = Dns.GetHostEntry("checkip.dyndns.org");
            IPAddress[] ips = hostEntry.AddressList;
            if (ips.Length == 0)
                throw new Exception("DNS无法解析checkip.dyndns.org的IP");
            string url = $"http://{ips[0]}";  // checkip.dyndns.org 有时候被墙，出错
            string response = await _HttpClient.GetStringAsync(url);
            string[] a = response.Split(':');
            if (a.Length == 1)
                throw new Exception("访问checkip.dyndns.org失败");
            string a2 = a[1].Substring(1);
            string[] a3 = a2.Split('<');
            string a4 = a3[0];
            return a4;
        }

        public static async Task<Record> query_record(string token, string domain, string subdomain)
        {
            var values = basicSettings(token, domain);

            var content = new FormUrlEncodedContent(values);
            var response = await _HttpClient.PostAsync("https://dnsapi.cn/Record.List", content);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto
            };
            //File.WriteAllText(@"d:\test.txt", json);
            var status_records = JsonConvert.DeserializeObject<StatusRecords>(json, settings);
            if (status_records.status.code != 1)
                throw new Exception(status_records.status.message);
            Record found_rec = null;
            if(status_records.records != null)
            {
                foreach(var rec in status_records.records)
                {
                    if(rec.type == "AAAA" && rec.name == subdomain)
                    {
                        found_rec = rec;
                        break;
                    }
                }
            }
            if (found_rec == null)
                throw new Exception("没找到对应的子域名");
            return found_rec;
        }

        public static async Task update_record(string token, string domain, Record record, string ip)
        {
            var values = basicSettings(token, domain);
            values.Add(new KeyValuePair<string, string>("record_id", record.id.ToString()));
            values.Add(new KeyValuePair<string, string>("sub_domain", record.name));
            values.Add(new KeyValuePair<string, string>("record_type", record.type));
            values.Add(new KeyValuePair<string, string>("record_line", record.line));
            values.Add(new KeyValuePair<string, string>("value", ip));

            var content = new FormUrlEncodedContent(values);
            var response = await _HttpClient.PostAsync(" https://dnsapi.cn/Record.Modify", content);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto
            };
            //File.WriteAllText(@"d:\test.txt", json);
            var status_record = JsonConvert.DeserializeObject<StatusRecord>(json, settings);
            if (status_record.status.code != 1)
                throw new Exception(status_record.status.message);
        }

        private static List<KeyValuePair<string, string>> basicSettings(string token, string domain)
        {
            // 设置请求头信息
            //_HttpClient.DefaultRequestHeaders.Add("Host", "www.oschina.net");
            _HttpClient.DefaultRequestHeaders.Add("Method", "Post");
            _HttpClient.DefaultRequestHeaders.Add("KeepAlive", "false");   // HTTP KeepAlive设为false，防止HTTP连接保持
            //设置UserAgent，参考API开发规范：https://www.dnspod.cn/docs/info.html#specification
            _HttpClient.DefaultRequestHeaders.Add("UserAgent",
                "dnspod_dotnet/1.0.0 (henryho2006@gmail.com; DNSPod.CN API v1.0.2016-08-15)");

            var values = new List<KeyValuePair<string, string>>();
            values.Add(new KeyValuePair<string, string>("login_token", token));
            values.Add(new KeyValuePair<string, string>("format", "json"));
            values.Add(new KeyValuePair<string, string>("lang", "cn"));
            values.Add(new KeyValuePair<string, string>("domain", domain));

            return values;
        }
 
    }
}
