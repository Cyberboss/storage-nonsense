using System;
using System.Collections.Generic;
using System.ComponentModel;
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
		private readonly List<IDirectoryInfo> tempDirectories = new List<IDirectoryInfo>();

		/// <inheritdoc />
		public Task StartAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("Listing all user temporary directory paths:");

			const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
			using var key = Registry.LocalMachine.OpenSubKey(ProfileListKey);
			if (key == null)
				throw new InvalidOperationException("Could not open ProfileList registry key.");

			foreach (var sidName in key.GetSubKeyNames())
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

				logger.LogInformation("\t- {tempPath}", tempPath);

				var directoryInfo = fileSystem.DirectoryInfo.New(tempPath);
				tempDirectories.Add(directoryInfo);
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync(CancellationToken cancellationToken)
			=> Task.WhenAll(tempDirectories.Select(RecursiveDirectoryDelete));

		// Import the MoveFileEx function from kernel32.dll
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

		// Flag instructing Windows to delay the move/delete until the next boot
		private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

		private async Task<bool> RecursiveDirectoryDelete(IDirectoryInfo directoryInfo)
		{
			await Task.Yield();

			List<Task<bool>> dependentTasks = directoryInfo.EnumerateDirectories().Select(RecursiveDirectoryDelete).ToList();

			if (!directoryInfo.Exists)
				return true;

			bool allDeleted = true;
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

			await Task.WhenAll(dependentTasks);

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
