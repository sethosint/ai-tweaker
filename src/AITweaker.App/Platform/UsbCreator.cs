using System.Text.Json;
using AITweaker.Core.Image;

namespace AITweaker.App.Platform;

public sealed record UsbDisk(int Number, string Name, long SizeBytes, string PartitionStyle);

/// <summary>Creates a bootable Windows 11 USB stick with autounattend.xml (MBR + FAT32, install.wim split when > 4 GB).</summary>
public static class UsbCreator
{
    public const string FidoUrl = "https://raw.githubusercontent.com/pbatard/Fido/master/Fido.ps1";

    public static async Task<List<UsbDisk>> ListAsync()
    {
        const string script = "@(Get-Disk | Where-Object { $_.BusType -eq 'USB' -and -not $_.IsBoot -and -not $_.IsSystem } | Select-Object Number, FriendlyName, Size, PartitionStyle) | ConvertTo-Json -Compress";
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromSeconds(60));
        var json = r.Output.Trim();
        if (json.Length == 0) return [];
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        return items.Select(e => new UsbDisk(
            e.GetProperty("Number").GetInt32(),
            e.GetProperty("FriendlyName").GetString() ?? "USB",
            e.GetProperty("Size").GetInt64(),
            e.GetProperty("PartitionStyle").ToString())).ToList();
    }

    /// <summary>Gets an official Windows 11 ISO link from Microsoft through Fido (the script Rufus uses).</summary>
    public static async Task<string> DownloadIsoAsync(string language, Action<string, double> progress, CancellationToken ct = default)
    {
        progress("fido", 0);
        var fido = Path.Combine(AppPaths.Downloads, "Fido.ps1");
        await Downloader.DownloadAsync(FidoUrl, fido, null, ct);
        // Fido is signed by its author (Akeo Consulting, the makers of Rufus): run it only when Windows confirms that
        await Downloader.RequireSignatureAsync(fido, "Akeo Consulting", ct);
        var script = $"& {ProcessRunner.PsQuote(fido)} -Win 11 -Rel Latest -Ed Pro -Lang {ProcessRunner.PsQuote(language)} -Arch x64 -GetUrl";
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(3), ct: ct);
        var url = r.Output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        if (url is null)
            throw new InvalidOperationException("Microsoft did not return a download link (region block or rate limit). Download the ISO manually and select it. " + (r.Error + r.Output).Trim());

        var iso = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", $"Win11_{language.Replace(' ', '_')}_x64.iso");
        Directory.CreateDirectory(Path.GetDirectoryName(iso)!);
        await Downloader.DownloadAsync(url, iso, (d, t) => progress("iso", t is > 0 ? (double)d / t.Value : 0), ct);
        return iso;
    }

    /// <param name="source">An official ISO, or the folder produced by <see cref="ImageBuilder"/>.</param>
    public static async Task CreateAsync(int diskNumber, string source, UnattendOptions options, Action<string, double> progress, CancellationToken ct = default)
    {
        bool isIso = source.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
        if (isIso ? !File.Exists(source) : !File.Exists(Path.Combine(source, "sources", "install.wim")))
            throw new FileNotFoundException("Windows image not found", source);
        var disks = await ListAsync();
        var disk = disks.FirstOrDefault(d => d.Number == diskNumber) ?? throw new InvalidOperationException($"Disk {diskNumber} is not a removable USB disk");
        if (disk.SizeBytes < 7_500_000_000) throw new InvalidOperationException("The USB stick must be at least 8 GB (16 GB recommended)");

        // holds the account password in plain text: it lives only until it is on the stick
        var unattend = Path.Combine(AppPaths.Downloads, $"autounattend-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(unattend, UnattendGenerator.Generate(options), ct);
        try
        {
            var self = options.CopyTweaker ? Environment.ProcessPath : null;
            long isoBytes = isIso ? new FileInfo(source).Length : Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

            AppLog.Warn("usb", $"erasing disk {disk.Number}: {disk.Name} ({disk.SizeBytes / 1_000_000_000} GB)");
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $N = {{diskNumber}}
                $Source = {{ProcessRunner.PsQuote(source)}}
                $IsIso = ${{isIso.ToString().ToLowerInvariant()}}
                $Unattend = {{ProcessRunner.PsQuote(unattend)}}
                $Tweaker = {{(self is null ? "$null" : ProcessRunner.PsQuote(self))}}
                $disk = Get-Disk -Number $N
                if ($disk.BusType -ne 'USB' -or $disk.IsBoot -or $disk.IsSystem) { throw 'Refusing to touch a non-USB or system disk' }
                Write-Output 'STEP:clean'
                if ($disk.PartitionStyle -ne 'RAW') { Clear-Disk -Number $N -RemoveData -RemoveOEM -Confirm:$false }
                Initialize-Disk -Number $N -PartitionStyle MBR
                # Windows refuses to format FAT32 volumes larger than 32 GB.
                $size = [Math]::Min((Get-Disk -Number $N).Size - 8MB, 32000MB)
                $part = New-Partition -DiskNumber $N -Size $size -IsActive -AssignDriveLetter
                Write-Output 'STEP:format'
                Format-Volume -Partition $part -FileSystem FAT32 -NewFileSystemLabel 'WIN11' -Confirm:$false | Out-Null
                $usb = (Get-Partition -DiskNumber $N -PartitionNumber $part.PartitionNumber).DriveLetter
                if (-not $usb) {
                  # automount is off on some PCs: ask for a letter explicitly
                  Add-PartitionAccessPath -DiskNumber $N -PartitionNumber $part.PartitionNumber -AssignDriveLetter
                  $usb = (Get-Partition -DiskNumber $N -PartitionNumber $part.PartitionNumber).DriveLetter
                }
                if (-not $usb) { throw 'The USB partition got no drive letter' }
                Write-Output "USB:$usb"
                Write-Output 'STEP:mount'
                if ($IsIso) {
                  $img = Mount-DiskImage -ImagePath $Source -PassThru
                  $letter = $null
                  for ($i = 0; $i -lt 15 -and -not $letter; $i++) { $letter = ($img | Get-Volume).DriveLetter; if (-not $letter) { Start-Sleep 1 } }
                  if (-not $letter) { Dismount-DiskImage -ImagePath $Source | Out-Null; throw 'The ISO was mounted but got no drive letter' }
                  $src = "$($letter):"
                } else {
                  $src = $Source.TrimEnd('\')
                }
                try {
                  Write-Output 'STEP:copy'
                  # "X:\." instead of "X:\": a trailing backslash before a quote would escape the quote for robocopy.
                  robocopy "$src\." "$($usb):\." /E /XF install.wim install.esd /A-:R /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
                  if ($LASTEXITCODE -ge 8) { throw "robocopy failed with code $LASTEXITCODE" }
                  $wim = "$src\sources\install.wim"
                  $esd = "$src\sources\install.esd"
                  $tmp = $null
                  # FAT32 holds files up to 4 GB: a bigger image is split; an ESD can't be split, so it becomes a WIM first
                  if (-not (Test-Path $wim) -and (Test-Path $esd) -and (Get-Item $esd).Length -ge 4GB) {
                    Write-Output 'STEP:split'
                    $tmp = Join-Path $env:TEMP 'ait-install.wim'
                    Remove-Item $tmp -ErrorAction SilentlyContinue
                    foreach ($i in (Get-WindowsImage -ImagePath $esd)) { Export-WindowsImage -SourceImagePath $esd -SourceIndex $i.ImageIndex -DestinationImagePath $tmp -CompressionType max | Out-Null }
                    $wim = $tmp
                  }
                  try {
                    if (Test-Path $wim) {
                      if ((Get-Item $wim).Length -ge 4GB) {
                        Write-Output 'STEP:split'
                        dism /Split-Image /ImageFile:"$wim" /SWMFile:"$($usb):\sources\install.swm" /FileSize:3800 | Out-Null
                        if ($LASTEXITCODE -ne 0) { throw "DISM split failed with code $LASTEXITCODE" }
                      } else {
                        Copy-Item $wim "$($usb):\sources\install.wim"
                      }
                    } elseif (Test-Path $esd) {
                      Copy-Item $esd "$($usb):\sources\install.esd"
                    }
                  } finally { if ($tmp) { Remove-Item $tmp -ErrorAction SilentlyContinue } }
                  Write-Output 'STEP:unattend'
                  Copy-Item $Unattend "$($usb):\autounattend.xml" -Force
                  if ($Tweaker) {
                    New-Item -ItemType Directory "$($usb):\AITweaker" -Force | Out-Null
                    Copy-Item $Tweaker "$($usb):\AITweaker\AITweaker.exe" -Force
                  }
                } finally {
                  if ($IsIso) { Dismount-DiskImage -ImagePath $Source | Out-Null }
                }
                Write-Output 'STEP:done'
                """;

            string? usbLetter = null;
            string step = "clean";
            using var poll = new CancellationTokenSource();
            var poller = Task.Run(async () =>
            {
                while (!poll.IsCancellationRequested)
                {
                    if (usbLetter is not null && step is "copy" or "split")
                    {
                        try
                        {
                            var d = new DriveInfo(usbLetter);
                            double used = d.TotalSize - d.AvailableFreeSpace;
                            progress(step, Math.Clamp(0.1 + 0.85 * used / isoBytes, 0.1, 0.95));
                        }
                        catch { /* drive not ready yet */ }
                    }
                    try { await Task.Delay(1000, poll.Token); } catch (OperationCanceledException) { }
                }
            });

            ProcessResult r;
            try
            {
                r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromHours(3), line =>
                {
                    if (line.StartsWith("USB:", StringComparison.Ordinal)) usbLetter = line[4..].Trim() + ":\\";
                    else if (line.StartsWith("STEP:", StringComparison.Ordinal))
                    {
                        step = line[5..].Trim();
                        double pct = step switch { "clean" => 0.02, "format" => 0.05, "mount" => 0.08, "copy" => 0.1, "unattend" => 0.97, "done" => 1.0, _ => -1 };
                        if (pct >= 0) progress(step, pct);
                        AppLog.Info("usb", step);
                    }
                }, ct);
            }
            finally
            {
                poll.Cancel();
                await poller;
                // cancelled while the ISO was mounted: the script never reached its own Dismount
                if (isIso) await ProcessRunner.PowerShellAsync($"Dismount-DiskImage -ImagePath {ProcessRunner.PsQuote(source)} -ErrorAction SilentlyContinue | Out-Null", TimeSpan.FromMinutes(1));
            }

            if (!r.Success || !r.Output.Contains("STEP:done"))
                throw new InvalidOperationException("USB creation failed: " + (r.Error.Length > 0 ? r.Error : r.Output).Trim());
            AppLog.Ok("usb", $"bootable USB ready on {usbLetter}");
        }
        finally
        {
            try { File.Delete(unattend); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
