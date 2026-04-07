using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PrintEase.App.Models;
using PrintDialog = System.Windows.Controls.PrintDialog;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;

namespace PrintEase.App.Services;

public sealed class PrinterManager
{
    public IReadOnlyList<PrinterDevice> GetPrinters()
    {
        using var localServer = new LocalPrintServer();
        var defaultPrinter = localServer.DefaultPrintQueue?.Name;

        return localServer.GetPrintQueues()
            .Select(queue =>
            {
                var portName = queue.QueuePort?.Name ?? "Unknown";
                var printerName = queue.Name ?? string.Empty;
                var (isOnline, isOffline) = DetermineAvailability(queue, portName);
                return new PrinterDevice
                {
                    Name = printerName,
                    PortName = portName,
                    IsNetwork = IsNetworkPrinter(portName),
                    IsVirtual = IsVirtualPrinter(printerName, portName),
                    IsOnline = isOnline,
                    IsOffline = isOffline,
                    IsDefault = string.Equals(defaultPrinter, queue.Name, StringComparison.OrdinalIgnoreCase)
                };
            })
            .OrderByDescending(p => p.IsOnline)
            .ThenByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PrintJobInfo> GetQueueJobs(string printerName)
    {
        var queue = new LocalPrintServer().GetPrintQueue(printerName);
        queue.Refresh();

        return queue.GetPrintJobInfoCollection()
            .Select(job => new PrintJobInfo
            {
                Id = job.JobIdentifier,
                Document = string.IsNullOrWhiteSpace(job.Name) ? "(Untitled)" : job.Name,
                User = string.IsNullOrWhiteSpace(job.Submitter) ? "Unknown" : job.Submitter,
                TotalPages = job.NumberOfPages,
                SubmittedOn = job.TimeJobSubmitted,
                Status = job.JobStatus.ToString()
            })
            .OrderBy(job => job.SubmittedOn)
            .ToList();
    }

    public void CancelJob(string printerName, int jobId)
    {
        var queue = new LocalPrintServer().GetPrintQueue(printerName);
        queue.Refresh();
        var job = queue.GetJob(jobId);
        job.Cancel();
    }

    public void CancelAllJobsFast(string printerName)
    {
        var queue = new LocalPrintServer().GetPrintQueue(printerName);
        queue.Refresh();

        // Pausing briefly before purge can make cancellation feel more immediate.
        queue.Pause();
        queue.Purge();
        queue.Resume();
    }

    public void PauseQueue(string printerName)
    {
        var queue = new LocalPrintServer().GetPrintQueue(printerName);
        queue.Pause();
    }

    public void ResumeQueue(string printerName)
    {
        var queue = new LocalPrintServer().GetPrintQueue(printerName);
        queue.Resume();
    }

    public bool TestConnection(string printerName, out string reason)
    {
        try
        {
            var queue = new LocalPrintServer().GetPrintQueue(printerName);
            if (DetermineOffline(queue))
            {
                reason = "Printer appears offline or unreachable.";
                return false;
            }

            if (queue.IsInError)
            {
                reason = "Printer reports an error state. Open properties for maintenance details.";
                return false;
            }

            reason = "Printer connection is healthy.";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Connection check failed: {ex.Message}";
            return false;
        }
    }

    private static bool DetermineOffline(PrintQueue queue)
    {
        var portName = queue.QueuePort?.Name ?? string.Empty;
        var (_, isOffline) = DetermineAvailability(queue, portName);
        return isOffline;
    }

    private static (bool IsOnline, bool IsOffline) DetermineAvailability(PrintQueue queue, string portName)
    {
        try
        {
            queue.Refresh();
        }
        catch
        {
            return (false, true);
        }

        if (queue.IsOffline || queue.IsNotAvailable || queue.IsInError)
        {
            return (false, true);
        }

        var status = queue.QueueStatus;
        if ((status & (PrintQueueStatus.Offline
                       | PrintQueueStatus.NotAvailable
                       | PrintQueueStatus.ServerUnknown
                       | PrintQueueStatus.Error
                       | PrintQueueStatus.PaperProblem
                       | PrintQueueStatus.PaperOut
                       | PrintQueueStatus.UserIntervention
                       | PrintQueueStatus.DoorOpen
                       | PrintQueueStatus.NoToner)) != 0)
        {
            return (false, true);
        }

        var printerName = queue.Name ?? string.Empty;

        if (IsVirtualPrinter(printerName, portName))
        {
            return (true, false);
        }

        if (IsNetworkPrinter(portName))
        {
            var host = TryExtractNetworkHost(portName, queue);
            if (!string.IsNullOrWhiteSpace(host) && !CanReachPrinterHost(host))
            {
                return (false, true);
            }

            try
            {
                queue.GetPrintCapabilities();
            }
            catch
            {
                // If host is known and capabilities fail, treat as offline; otherwise leave unknown.
                return string.IsNullOrWhiteSpace(host) ? (false, false) : (false, true);
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                // Keep unresolved network queues as unknown, not hard-offline.
                return (false, false);
            }

            return (true, false);
        }

        return (true, false);
    }

    private static bool IsVirtualPrinter(string printerName, string portName)
    {
        var text = $"{printerName} {portName}";
        return text.Contains("PDF", StringComparison.OrdinalIgnoreCase)
            || text.Contains("XPS", StringComparison.OrdinalIgnoreCase)
            || text.Contains("OneNote", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Fax", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractNetworkHost(string portName, PrintQueue queue)
    {
        static string? ExtractHost(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            var text = source.Trim();

            if (text.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
            {
                return text[3..].Trim();
            }

            if (text.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
            {
                var withoutSlashes = text[2..];
                var separatorIndex = withoutSlashes.IndexOf('\\');
                return separatorIndex > 0 ? withoutSlashes[..separatorIndex] : withoutSlashes;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host;
            }

            var ipMatch = Regex.Match(text, @"\b((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)\b");
            if (ipMatch.Success)
            {
                return ipMatch.Value;
            }

            if (!text.Contains(' ')
                && text.Any(char.IsLetterOrDigit)
                && (text.Contains('.') || text.Contains('-')))
            {
                return text;
            }

            return null;
        }

        return ExtractHost(portName)
            ?? ExtractHost(queue.Name)
            ?? ExtractHost(queue.FullName)
            ?? ExtractHost(queue.ShareName)
            ?? ExtractHost(queue.Comment)
            ?? ExtractHost(queue.Location);
    }

    private static bool CanReachPrinterHost(string host)
    {
        if (CanPingHost(host))
        {
            return true;
        }

        // Many printers block ping but keep raw/IPP ports reachable.
        return CanConnectTcp(host, 9100, 900) || CanConnectTcp(host, 631, 900);
    }

    private static bool CanPingHost(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, 800);
            return reply is not null && reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanConnectTcp(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = connectTask.Wait(timeoutMs);
            return completed && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public void PrintTestPage(string printerName, PrintOptions options)
    {
        var dialog = new PrintDialog
        {
            PrintQueue = new LocalPrintServer().GetPrintQueue(printerName)
        };

        dialog.PrintTicket = dialog.PrintQueue.DefaultPrintTicket;
        dialog.PrintTicket.PageOrientation = options.Orientation;
        dialog.PrintTicket.OutputQuality = options.OutputQuality;
        dialog.PrintTicket.PageMediaSize = BuildPageMediaSize(options);

        var document = BuildSimpleDocument("PrintEase Test Page", options, DateTime.Now.ToString("F"));
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "PrintEase test page");
    }

    public IReadOnlyList<string> BuildPreviewPages(string content)
    {
        var normalized = string.IsNullOrWhiteSpace(content)
            ? "(Empty content)"
            : content.Replace("\r\n", "\n", StringComparison.Ordinal);

        const int charsPerPage = 1800;
        var pages = new List<string>();

        for (var i = 0; i < normalized.Length; i += charsPerPage)
        {
            var length = Math.Min(charsPerPage, normalized.Length - i);
            pages.Add(normalized.Substring(i, length));
        }

        if (pages.Count == 0)
        {
            pages.Add("(Empty content)");
        }

        return pages;
    }

    public void PrintSampleContent(string printerName, PrintOptions options, string content, int? pageRangeStart, int? pageRangeEnd)
    {
        var dialog = new PrintDialog
        {
            PrintQueue = new LocalPrintServer().GetPrintQueue(printerName)
        };

        dialog.PrintTicket = dialog.PrintQueue.DefaultPrintTicket;
        dialog.PrintTicket.PageOrientation = options.Orientation;
        dialog.PrintTicket.OutputQuality = options.OutputQuality;
        dialog.PrintTicket.CopyCount = options.Copies;
        dialog.PrintTicket.PageMediaSize = BuildPageMediaSize(options);

        var pages = BuildPreviewPages(content);
        var (start, end) = NormalizePageRange(pageRangeStart, pageRangeEnd, pages.Count);
        var selectedPages = pages.Skip(start - 1).Take(end - start + 1).ToList();

        var document = BuildPagedDocument(selectedPages, options, start, $"Printed via PrintEase on {DateTime.Now:F}");
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "PrintEase custom print");
    }

    public void PrintFiles(string printerName, IReadOnlyList<string> filePaths, int copies)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        var attempts = Math.Max(1, copies);
        foreach (var path in filePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            for (var i = 0; i < attempts; i++)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Verb = "printto",
                    Arguments = $"\"{printerName}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
            }
        }
    }

    public void OpenPrinterProperties(string printerName)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"printui.dll,PrintUIEntry /p /n \"{printerName}\"",
            UseShellExecute = true
        });
    }

    public void OpenPrintersSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:printers",
            UseShellExecute = true
        });
    }

    public async Task<string> RestartSpoolerAsync()
    {
        // Restarting the spooler clears stale jobs/drivers that often block cancellation.
        var stopResult = await RunShellCommandAsync("sc stop spooler");
        var startResult = await RunShellCommandAsync("sc start spooler");
        return $"Stop spooler: {stopResult}\nStart spooler: {startResult}";
    }

    private static bool IsNetworkPrinter(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return false;
        }

        var networkHints = new[] { "WSD", "IP_", "TCP", "HTTP", "IPP", "\\" };
        return networkHints.Any(hint => portName.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static (int Start, int End) NormalizePageRange(int? pageRangeStart, int? pageRangeEnd, int pageCount)
    {
        if (pageCount < 1)
        {
            return (1, 1);
        }

        var start = pageRangeStart.GetValueOrDefault(1);
        var end = pageRangeEnd.GetValueOrDefault(pageCount);

        start = Math.Clamp(start, 1, pageCount);
        end = Math.Clamp(end, 1, pageCount);

        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (start, end);
    }

    private static PageMediaSize BuildPageMediaSize(PrintOptions options)
    {
        if (string.Equals(options.PaperSizeName, "custom", StringComparison.OrdinalIgnoreCase)
            && options.CustomPaperWidthInches.HasValue
            && options.CustomPaperHeightInches.HasValue
            && options.CustomPaperWidthInches.Value > 0
            && options.CustomPaperHeightInches.Value > 0)
        {
            return new PageMediaSize(options.CustomPaperWidthInches.Value * 96d, options.CustomPaperHeightInches.Value * 96d);
        }

        return options.PaperSizeName.ToLowerInvariant() switch
        {
            "a4" => new PageMediaSize(PageMediaSizeName.ISOA4),
            "letter" => new PageMediaSize(PageMediaSizeName.NorthAmericaLetter),
            "legal" => new PageMediaSize(PageMediaSizeName.NorthAmericaLegal),
            "a3" => new PageMediaSize(PageMediaSizeName.ISOA3),
            "a5" => new PageMediaSize(PageMediaSizeName.ISOA5),
            "8x13" => new PageMediaSize(8d * 96d, 13d * 96d),
            _ => new PageMediaSize(PageMediaSizeName.ISOA4)
        };
    }

    private static FlowDocument BuildSimpleDocument(string title, PrintOptions options, string footer)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            PagePadding = new Thickness(options.MarginLeft, options.MarginTop, options.MarginRight, options.MarginBottom),
            ColumnWidth = double.PositiveInfinity
        };

        document.Blocks.Add(new Paragraph(new Run(title))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        document.Blocks.Add(new Paragraph(new Run("This page validates connectivity, queue responsiveness, and print settings."))
        {
            Margin = new Thickness(0, 0, 0, 20)
        });

        document.Blocks.Add(new Paragraph(new Run(footer))
        {
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.DimGray
        });

        return document;
    }

    private static FlowDocument BuildPagedDocument(IReadOnlyList<string> pages, PrintOptions options, int startPageNumber, string footer)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(options.MarginLeft, options.MarginTop, options.MarginRight, options.MarginBottom),
            ColumnWidth = double.PositiveInfinity
        };

        for (var i = 0; i < pages.Count; i++)
        {
            var actualPageNumber = startPageNumber + i;

            document.Blocks.Add(new Paragraph(new Run($"Page {actualPageNumber}"))
            {
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                BreakPageBefore = i > 0
            });

            document.Blocks.Add(new Paragraph(new Run(pages[i]))
            {
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        document.Blocks.Add(new Paragraph(new Run(footer))
        {
            Foreground = Brushes.DimGray,
            FontStyle = FontStyles.Italic
        });

        return document;
    }

    private static async Task<string> RunShellCommandAsync(string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var text = string.IsNullOrWhiteSpace(error) ? output : error;
        return string.IsNullOrWhiteSpace(text) ? "OK" : text.Trim();
    }
}
