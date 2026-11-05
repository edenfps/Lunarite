using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Lunarite.Resources;

namespace Lunarite.Network;

// TcpListener avoids Windows HttpListener URL-ACL requirements, so an ordinary server process can
// serve the retail client's fixed 127.0.0.1:80 ProductTransfer endpoint.
public sealed class CashShopWebServer : IDisposable
{
    private readonly CashShopCatalog _cashShopCatalog;

    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    public CashShopWebServer(CashShopCatalog cashShopCatalog)
    {
        _cashShopCatalog = cashShopCatalog;
    }

    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    public bool Start(int port = 80)
    {
        try
        {
            // HUXLEY_SHOP_BIND=0.0.0.0 on a public server: players' clients fetch the catalog from
            // the address the patch server writes into m_strShopServerIP.
            _listener = new TcpListener(IPAddress.TryParse(Environment.GetEnvironmentVariable("HUXLEY_SHOP_BIND"),
                out var bind) ? bind : IPAddress.Loopback, port);
            _listener.Start();
            _stop = new CancellationTokenSource();
            _ = AcceptLoop(_stop.Token);
            Log.WriteSuccess("[CashShop] serving free ProductTransfer catalog at http://127.0.0.1:{0}/ss/", Port);
            return true;
        }
        catch (Exception ex)
        {
            Log.WriteWarning("[CashShop] local catalog listener unavailable: {0}", ex.Message);
            return false;
        }
    }

    private async Task AcceptLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(stop);
                _ = Task.Run(() => Respond(client), stop);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log.WriteWarning("[CashShop] HTTP accept failed: {0}", ex.Message); }
        }
    }

    private void Respond(TcpClient client)
    {
        using (client)
        try
        {
            NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            string request = reader.ReadLine() ?? "";
            string[] requestParts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string path = requestParts.Length >= 2
                ? Uri.UnescapeDataString(requestParts[1].Split('?')[0]) : "/";
            while (!string.IsNullOrEmpty(reader.ReadLine())) { }

            string file = Path.GetFileName(path);
            string body;
            string contentType;
            int status = 200;
            if (file.Equals("IBSCategory.txt", StringComparison.OrdinalIgnoreCase))
                (body, contentType) = (CashShopCatalog.CategoryFile(), "text/plain; charset=utf-8");
            else if (file.Equals("IBSPackage.txt", StringComparison.OrdinalIgnoreCase))
                (body, contentType) = (_cashShopCatalog.PackageFile(), "text/plain; charset=utf-8");
            else if (file.Equals("IBSProduct.txt", StringComparison.OrdinalIgnoreCase))
                (body, contentType) = (_cashShopCatalog.ProductFile(), "text/plain; charset=utf-8");
            else if (path.StartsWith("/ss", StringComparison.OrdinalIgnoreCase))
                (body, contentType) = (_cashShopCatalog.IndexHtml(), "text/html; charset=utf-8");
            else
                (body, contentType, status) = ("Not found", "text/plain; charset=utf-8", 404);

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n"
                + $"Content-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            stream.Write(header); stream.Write(bytes);
        }
        catch (Exception ex) { Log.WriteWarning("[CashShop] HTTP response failed: {0}", ex.Message); }
    }

    public void Dispose()
    {
        _stop?.Cancel();
        _listener?.Stop();
        _stop?.Dispose();
    }
}
