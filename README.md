# Storage Nonsense

This simple Windows service completely wipes all user `%TEMP%` directories upon PC shutdown.

The name is a play on [Windows Storage Sense](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/manage-drive-space-with-storage-sense) which, for all its """intelligence""", doesn't do this simple maintenance task.

## Did you know that the count of files in your temp directory can significantly slow down Windows 10 startup?

I clocked mine at going from **2 minutes** at its worst to **5 seconds** after deleting my temp directory.

## Usage

1. Place [StorageNonsense.exe](https://github.com/Cyberboss/storage-nonsense/releases/latest) somewhere permanent (henceforth specified as `C:\path\to\bin`).
1. Open an admin command prompt.
1. Run `sc.exe create "Storage Nonsense" binpath="C:\path\to\bin\StorageNonsense.exe"`.
1. Open the services control panel (`Win + R` => `services.msc`).
1. Find the `Storage Nonsense` service, right-click it, and select `Properties`.
1. Set `Startup type` to `Automatic` and click `Start`.
1. Go to the `Log On` tab and ensure `Log on as` is set to `Local System`
1. (Optional) Open the registry and navigate to `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control`. Set `WaitToKillServiceTimeout` somewhere above the default 10000 milliseconds (10s) timeout to allow the service to do its work.

Verbose logs can be found in the Windows Event Viewer under `Windows/Application`.

## I'm paranoid, what if this is a virus?

The binaries in the Releases are generated and attested by GitHub actions. Feel free to inspect the source code yourself.

Alternatively, clone the repo and build it yourself. You'll need the [.NET 10+ SDK](https://dotnet.microsoft.com/en-us/download).

```
dotnet publish /nowarn:NETSDK1194 -o C:\path\to\bin
```
