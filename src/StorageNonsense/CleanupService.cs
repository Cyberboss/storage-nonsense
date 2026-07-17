using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace StorageNonsense
{
	sealed class CleanupService(IFileSystem fileSystem, ILogger<CleanupService> logger) : IHostedService
	{
		private readonly IFileSystem fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		private readonly ILogger<CleanupService> logger = logger ?? throw new ArgumentNullException(nameof(logger));

		/// <inheritdoc />
		public Task StartAsync(CancellationToken cancellationToken)
			=> Task.CompletedTask;

		/// <inheritdoc />
		public async Task StopAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("Listing all user temporary directory paths:");

			const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
			using var key = Registry.LocalMachine.OpenSubKey(ProfileListKey);
			if (key == null)
			{
				logger.LogError("Could not open ProfileList registry key.");
				return;
			}

			var subKeyNames = key.GetSubKeyNames();
			List<IDirectoryInfo> tempDirectories = new List<IDirectoryInfo>(subKeyNames.Length);
			foreach (var sidName in subKeyNames)
			{
				using var profileKey = key.OpenSubKey(sidName);
				if (profileKey == null)
					continue;

				var profilePath = profileKey.GetValue("ProfileImagePath") as string;
				if (String.IsNullOrWhiteSpace(profilePath))
					continue;

				// Expand any environment variables (rare, but can appear)
				profilePath = Environment.ExpandEnvironmentVariables(profilePath);

				var tempPath = fileSystem.Path.Combine(profilePath, "AppData", "Local", "Temp");

				logger.LogInformation("{sidName}: {tempPath}", sidName, tempPath);

				var directoryInfo = fileSystem.DirectoryInfo.New(tempPath);
				tempDirectories.Add(directoryInfo);
			}

			logger.LogInformation("Starting deletion...");
			var stopwatch = Stopwatch.StartNew();
			await Task.WhenAll(tempDirectories.Select(RecursiveDirectoryDelete));
			stopwatch.Stop();
			logger.LogInformation("Deletion took {seconds}s", stopwatch.Elapsed.TotalSeconds);
		}

		// Import the MoveFileEx function from kernel32.dll
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

		// Flag instructing Windows to delay the move/delete until the next boot
		private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

		private async Task<bool> RecursiveDirectoryDelete(IDirectoryInfo directoryInfo)
		{
			await Task.Yield();

			if (!directoryInfo.Exists)
				return true;

			List<Task<bool>> dependentTasks = directoryInfo.EnumerateDirectories().Select(RecursiveDirectoryDelete).ToList();

			var allDeleted = true;
			foreach (var file in directoryInfo.EnumerateFiles())
				try
				{
					file.Delete();
				}
				catch (Exception ex)
				{
					allDeleted = false;
					logger.LogWarning(ex, "File \"{fullName}\" is resistant to deletion, trying kernel post-shutdown delete", file.FullName);

					if (!MoveFileEx(file.FullName, null, MOVEFILE_DELAY_UNTIL_REBOOT))
						logger.LogError(new Win32Exception(Marshal.GetLastPInvokeError()), "Failed to delete file on shutdown: {fullName}", file.FullName);
				}

			try
			{
				await Task.WhenAll(dependentTasks);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error in recursive task!");
			}

			if (allDeleted && dependentTasks.All(task => task.Result))
			{
				try
				{
					directoryInfo.Delete();
					return true;
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to delete empty directory: {fullName}", directoryInfo.FullName);
				}
			}

			return false;
		}
	}
}
