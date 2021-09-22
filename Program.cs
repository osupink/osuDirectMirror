using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace OsuDirectMirror
{
    class Program
    {
        public delegate bool ConsoleCtrlDelegate(int dwCtrlType);
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);
        static ConsoleCtrlDelegate exitHandler = new ConsoleCtrlDelegate(ExitHandler);
        static HostsFile hostsFile = new HostsFile();
        static ProxyServer proxyServer = new ProxyServer();
        static string CurOsuDirectMirrorVer = "b20210922.1";
        static HttpClientHandler osuHTTPClientHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
        };
        static HttpClient osuHTTPClient = new HttpClient(osuHTTPClientHandler);

        private static bool ExitHandler(int CtrlType)
        {
            if (proxyServer.ProxyRunning)
            {
                try
                {
                    proxyServer.Stop();
                }
                catch { }
            }
            hostsFile.Remove();
            return false;
        }
        private static async Task OnRequest(object sender, SessionEventArgs e)
        {
            /*
            if (!e.HttpClient.Request.Url.Contains("ppy.sh"))
            {
                e.GenericResponse("", HttpStatusCode.BadGateway);
            }
            */
            Uri requestUri = e.HttpClient.Request.RequestUri;
#if DEBUG
            Console.WriteLine("URI: " + requestUri.OriginalString);
#endif
            switch (e.HttpClient.Request.Host)
            {
                case "osu.ppy.sh":
                    if (requestUri.AbsolutePath.StartsWith("/d/"))
                    {
                        string redirectURI = "https://txy1.sayobot.cn/beatmaps/download/" + requestUri.AbsolutePath.Substring(3);
#if DEBUG
                        Console.WriteLine("Redirect: " + redirectURI);
#endif
                        e.Redirect(redirectURI);
                        return;
                    }
                    try
                    {
                        UriBuilder modUri = new UriBuilder(e.HttpClient.Request.RequestUri);
                        modUri.Host = "104.22.74.180";
                        HttpRequestMessage httpReqMessage = new HttpRequestMessage(new HttpMethod(e.HttpClient.Request.Method), modUri.Uri);
                        switch (e.HttpClient.Request.Method.ToUpper())
                        {
                            case "PUT":
                            case "POST":
                            case "PATCH":
                                byte[] bodyBytes = await e.GetRequestBody();
                                if (bodyBytes != null && bodyBytes.Length > 0)
                                {
                                    httpReqMessage.Content = new ByteArrayContent(await e.GetRequestBody());
                                }
                                break;
                            default:
                                break;
                        }
                        //httpReqMessage.Content = new ByteArrayContent(await e.GetRequestBody());
                        List<HttpHeader> headers = e.HttpClient.Request.Headers.GetAllHeaders();
                        foreach (HttpHeader header in headers)
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(header.Value))
                                {
                                    continue;
                                }
                                switch (header.Name.ToLower())
                                {
                                    case "host":
                                    case "connection":
                                    case "accept-encoding":
                                        continue;
                                    case "content-length":
                                        if (httpReqMessage != null)
                                        {
                                            httpReqMessage.Content.Headers.ContentLength = long.Parse(header.Value);
                                        }
                                        continue;
                                    case "content-type":
                                        if (httpReqMessage != null)
                                        {
                                            httpReqMessage.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(header.Value);
                                        }
                                        continue;
                                    default:
                                        break;
                                }
                                if (!httpReqMessage.Headers.Contains(header.Name))
                                {
                                    httpReqMessage.Headers.Add(header.Name, header.Value);
                                }
                            }
                            catch (Exception he)
                            {
                                Console.WriteLine(he);
                                Console.WriteLine(header.Name);
                            }
                        }
                        HttpResponseMessage httpResponseMessage = await osuHTTPClient.SendAsync(httpReqMessage);
                        List<HttpHeader> IDHeaderList = new List<HttpHeader>();
                        foreach (KeyValuePair<string, IEnumerable<string>> h in httpResponseMessage.Headers.Concat(httpResponseMessage.Content.Headers))
                        {
                            switch (h.Key.ToLower())
                            {
                                case "alt-svc":
                                    continue;
                                default:
                                    break;
                            }
                            foreach (var c in h.Value)
                            {
                                HttpHeader hh = new HttpHeader(h.Key, c);
                                IDHeaderList.Add(hh);
                            }
                        }
                        IEnumerable<HttpHeader> IDHeader = IDHeaderList.AsEnumerable();
                        e.GenericResponse(await httpResponseMessage.Content.ReadAsByteArrayAsync(), httpResponseMessage.StatusCode, IDHeader);
                    }
                    catch (Exception re)
                    {
                        Console.WriteLine("Exception: " + re);
                        Console.WriteLine(re.InnerException);
                    }
                    return;
            }
        }
        private static void Main(string[] args)
        {
            SetConsoleCtrlHandler(exitHandler, true);
#if DEBUG
            CurOsuDirectMirrorVer += "dev";
#endif
            string ProgramTitle = string.Format("osuDirectMirror 客户端 ({0})", CurOsuDirectMirrorVer);
            Console.Title = ProgramTitle;
            Console.WriteLine("osuDirectMirror 客户端初始化...");
            CertManager.InstallCertificate("cert/ca.crt", System.Security.Cryptography.X509Certificates.StoreName.Root);
            CertManager.InstallCertificate("cert/osu.crt", System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority);
            osuHTTPClient.DefaultRequestHeaders.Host = "osu.ppy.sh";
            osuHTTPClient.DefaultRequestHeaders.Connection.Add("keep-alive");
            proxyServer.ReuseSocket = false;
            proxyServer.BeforeRequest += OnRequest;
            TransparentProxyEndPoint httpEndPoint = new TransparentProxyEndPoint(IPAddress.Any, 80, false);
            TransparentProxyEndPoint httpsEndPoint = new TransparentProxyEndPoint(IPAddress.Any, 443, true)
            {
                GenericCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2("cert/ppy.sh.pfx")
            };
            proxyServer.AddEndPoint(httpEndPoint);
            proxyServer.AddEndPoint(httpsEndPoint);

            HostsEntry[] hostsEntry = new HostsEntry[]  {
                new HostsEntry { domain="osu.ppy.sh", ip="127.0.0.1" }
            };
            try
            {
                hostsFile.Write(hostsEntry);
            }
            catch
            {
                Console.WriteLine("访问被拒绝，请关闭你的杀毒软件然后再试一次.");
                Console.ReadKey(true);
                return;
            }

            Console.WriteLine("正在启动 osuDirectMirror 客户端...");
            try
            {
                proxyServer.Start();
            }
            catch (Exception e)
            {
                if (e.InnerException.GetType() == typeof(System.Net.Sockets.SocketException))
                {
                    Console.WriteLine("端口已被占用，如没有重复打开客户端，请使用 FixTool 工具来进行修复.");
                }
                else
                {
                    Console.WriteLine("Exception: " + e);
                    Console.WriteLine(e.InnerException);
                }
                Console.ReadKey(true);
                return;
            }
            Console.WriteLine("启动完成!");
            while (true)
            {
                Console.ReadKey(true);
            }
        }
    }
}
