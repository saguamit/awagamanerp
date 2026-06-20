using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using NUnit.Framework;

namespace Awagaman_ERP.Tests
{
    internal sealed class WinAppDriverClient : IDisposable
    {
        private const string WinAppDriverExe = @"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe";
        private const string BaseUrl = "http://127.0.0.1:4723/";
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        private readonly HttpClient _client;
        private readonly Process _appProcess;

        public string SessionId { get; }

        public WinAppDriverClient(string appPath)
        {
            EnsureServerRunning();

            _client = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl)
            };

            _appProcess = StartApp(appPath);
            SessionId = CreateSession(_appProcess);
        }

        public void Dispose()
        {
            try
            {
                if (_appProcess != null && !_appProcess.HasExited)
                {
                    try
                    {
                        _appProcess.Kill();
                        _appProcess.WaitForExit(5000);
                    }
                    catch
                    {
                        // ignore cleanup failures
                    }
                    finally
                    {
                        _appProcess.Dispose();
                    }
                }

                if (!string.IsNullOrWhiteSpace(SessionId))
                {
                    var response = _client.DeleteAsync($"session/{SessionId}").GetAwaiter().GetResult();
                    response.Dispose();
                }
            }
            catch
            {
                // Ignore cleanup failures in UI smoke tests.
            }
            finally
            {
                _client.Dispose();
            }
        }

        public string GetWindowTitle()
        {
            var json = SendGet($"session/{SessionId}/title");
            return ReadString(json, "value");
        }

        public bool TryFindElement(string strategy, string value, out string elementId)
        {
            var body = new Dictionary<string, object>
            {
                ["using"] = strategy,
                ["value"] = value
            };

            var json = SendPost($"session/{SessionId}/element", body);
            elementId = ReadElementId(json);
            return !string.IsNullOrWhiteSpace(elementId);
        }

        public string FindElement(string strategy, string value)
        {
            if (!TryFindElement(strategy, value, out var elementId))
            {
                throw new AssertionException($"Unable to find element using '{strategy}' = '{value}'.");
            }

            return elementId;
        }

        public void Click(string elementId)
        {
            SendPost($"session/{SessionId}/element/{elementId}/click", new Dictionary<string, object>());
        }

        public bool IsDisplayed(string elementId)
        {
            var json = SendGet($"session/{SessionId}/element/{elementId}/displayed");
            return ReadBool(json, "value");
        }

        public string GetText(string elementId)
        {
            var json = SendGet($"session/{SessionId}/element/{elementId}/text");
            return ReadString(json, "value");
        }

        private static void EnsureServerRunning()
        {
            if (IsServerReady())
            {
                return;
            }

            if (!File.Exists(WinAppDriverExe))
            {
                throw new InvalidOperationException($"WinAppDriver.exe not found at '{WinAppDriverExe}'.");
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = WinAppDriverExe,
                    Arguments = "127.0.0.1 4723",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();

            if (process.WaitForExit(3000))
            {
                var output = (process.StandardOutput.ReadToEnd() ?? string.Empty) + Environment.NewLine + (process.StandardError.ReadToEnd() ?? string.Empty);
                if (output.IndexOf("Developer mode is not enabled", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        "WinAppDriver cannot start because Windows Developer Mode is disabled. " +
                        "Enable Developer Mode in Windows Settings, restart Windows Application Driver, then rerun the UI smoke tests.");
                }

                throw new InvalidOperationException(
                    "WinAppDriver exited immediately during startup. Output: " + output.Trim());
            }

            WaitForServerReady(TimeSpan.FromSeconds(20));
        }

        private static bool IsServerReady()
        {
            try
            {
                using (var client = new HttpClient { BaseAddress = new Uri(BaseUrl) })
                {
                    var response = client.GetAsync("status").GetAwaiter().GetResult();
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void WaitForServerReady(TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (IsServerReady())
                {
                    return;
                }

                Thread.Sleep(500);
            }

            throw new TimeoutException("WinAppDriver did not become ready in time.");
        }

        private static void WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (condition())
                {
                    return;
                }

                Thread.Sleep(500);
            }

            throw new TimeoutException(failureMessage);
        }

        private Process StartApp(string appPath)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = appPath,
                WorkingDirectory = Path.GetDirectoryName(appPath),
                UseShellExecute = false
            });

            if (process == null)
            {
                throw new InvalidOperationException($"Unable to start app '{appPath}'.");
            }

            WaitFor(() =>
            {
                try
                {
                    process.Refresh();
                    return process.MainWindowHandle != IntPtr.Zero;
                }
                catch
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(30), "The application window did not appear in time.");

            return process;
        }

        private string CreateSession(Process appProcess)
        {
            appProcess.Refresh();
            var mainWindowHandle = appProcess.MainWindowHandle;
            if (mainWindowHandle == IntPtr.Zero)
            {
                WaitFor(() =>
                {
                    try
                    {
                        appProcess.Refresh();
                        mainWindowHandle = appProcess.MainWindowHandle;
                        return mainWindowHandle != IntPtr.Zero;
                    }
                    catch
                    {
                        return false;
                    }
                }, TimeSpan.FromSeconds(30), "The application window handle was not available in time.");
            }

            var body = new Dictionary<string, object>
            {
                ["desiredCapabilities"] = new Dictionary<string, object>
                {
                    ["platformName"] = "Windows",
                    ["deviceName"] = "WindowsPC",
                    ["appTopLevelWindow"] = mainWindowHandle.ToString("x")
                }
            };

            var json = SendPost("session", body);
            var sessionId = ReadString(json, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                var value = ReadObject(json, "value");
                if (value is Dictionary<string, object> valueDict)
                {
                    sessionId = ReadString(valueDict, "sessionId");
                }
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("WinAppDriver session could not be created.");
            }

            return sessionId;
        }

        private string SendGet(string relativePath)
        {
            var response = _client.GetAsync(relativePath).GetAwaiter().GetResult();
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"WinAppDriver GET {relativePath} failed: {response.StatusCode} {text}");
            }

            return text;
        }

        private string SendPost(string relativePath, object body)
        {
            var json = Serializer.Serialize(body);
            var response = _client.PostAsync(relativePath, new StringContent(json, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"WinAppDriver POST {relativePath} failed: {response.StatusCode} {text}");
            }

            return text;
        }

        private static string ReadString(object json, string key)
        {
            if (json is Dictionary<string, object> dict && dict.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }

            return null;
        }

        private static string ReadString(string json, string key)
        {
            return ReadString(Serializer.DeserializeObject(json), key);
        }

        private static bool ReadBool(string json, string key)
        {
            var value = ReadObject(json, key);
            if (value is bool b)
            {
                return b;
            }

            return bool.TryParse(value?.ToString(), out var parsed) && parsed;
        }

        private static object ReadObject(string json, string key)
        {
            if (Serializer.DeserializeObject(json) is Dictionary<string, object> dict && dict.TryGetValue(key, out var value))
            {
                return value;
            }

            return null;
        }

        private static string ReadElementId(string json)
        {
            var root = Serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                return null;
            }

            if (root.TryGetValue("value", out var valueObj))
            {
                if (valueObj is Dictionary<string, object> valueDict)
                {
                    if (valueDict.TryGetValue("ELEMENT", out var legacyElementId))
                    {
                        return legacyElementId?.ToString();
                    }

                    if (valueDict.TryGetValue("element-6066-11e4-a52e-4f735466cecf", out var w3cElementId))
                    {
                        return w3cElementId?.ToString();
                    }
                }
            }

            if (root.TryGetValue("ELEMENT", out var rootLegacy))
            {
                return rootLegacy?.ToString();
            }

            if (root.TryGetValue("element-6066-11e4-a52e-4f735466cecf", out var rootW3c))
            {
                return rootW3c?.ToString();
            }

            return null;
        }
    }
}
