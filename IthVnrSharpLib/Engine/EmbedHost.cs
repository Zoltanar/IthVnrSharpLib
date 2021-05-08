using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace IthVnrSharpLib.Engine
{
	public class EmbedHost : IDisposable
	{
		private const string PipeName = @"vnr.socket";

		private const string PipeHostThreadName = nameof(IthVnrSharpLib) + "." + nameof(Engine) + "." + nameof(EmbedHost) + "." + nameof(ServerThread);

		public static readonly string[] AgentDlls =
		{
			// ReSharper disable StringLiteralTypo
			// ReSharper disable CommentTypo
			//these are debug dlls, maybe use condition to use release libraries in release build.
			"ucrtbased.dll",
			"vcruntime140d.dll",
			"msvcp140d.dll",
			"Qt5Cored.dll", /*depends on vcruntime, msvcp*/ 
			"Qt5Networkd.dll", /*depends on qtcore*/
			"Qt5Guid.dll", /*depends on qtcore*/
			"vnragent.dll"
			// ReSharper restore CommentTypo
			// ReSharper restore StringLiteralTypo
		};

		private string _engineName = "Unset";
		private NamedPipeServerStream _pipeServer;
		private Thread _listeningThread;
		private readonly IthVnrViewModel _mainViewModel;
		public bool Initialized { get; private set; }
		private int? _connectedProcessId;

		public EmbedHost(IthVnrViewModel mainViewModel)
		{
			_mainViewModel = mainViewModel;
		}

		public void Initialize()
		{
			StaticHelpers.LogToDebug($"{nameof(IthVnrSharpLib)}.{nameof(EmbedHost)}.{nameof(Initialize)}");
			try
			{
				_pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);
				_listeningThread = new Thread(async () => await ServerThread()) { Name = PipeHostThreadName };
				_listeningThread.Start();
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			Initialized = true;
		}

		public async Task<bool> SendSettings()
		{
			if (!_pipeServer.IsConnected) return false;
			try
			{
				var data = GetDataBytes("settings", new EmbedSettings());
				var allBytes = BitConverter.GetBytes(data.Length).Reverse().Concat(data).ToArray();
				await _pipeServer.WriteAsync(allBytes, 0, allBytes.Length, _threadToken.Token);
				return true;
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				return false;
			}
		}

		private static byte[] GetDataBytes(params object[] data)
		{
			var size = data.Length;
			var head = new List<uint>();
			var body = new List<string>();
			head.Add((uint)size);
			foreach (var dataPart in data)
			{
				var text = dataPart as string ?? JsonConvert.SerializeObject(dataPart);
				head.Add((uint)text.Length);
				body.Add(text);
			}
			var sendData = head.SelectMany(PackUInt32).Concat(Encoding.UTF8.GetBytes(string.Join("", body))).ToArray(); //("", (body));
			return sendData;
		}

		private static byte[] PackUInt32(uint i)
		{
			var byte1 = ((i >> 24) & 0xff);
			var byte2 = ((i >> 16) & 0xff);
			var byte3 = ((i >> 8) & 0xff);
			var byte4 = (i & 0xff);
			return new[] { (byte)byte1, (byte)byte2, (byte)byte3, (byte)byte4 };
		}

		private static uint UnpackUInt32(byte[] data, int index)
		{
			var uint32 = BitConverter.ToUInt32(new[] { data[index + 3], data[index + 2], data[index + 1], data[index] }, 0);
			return uint32;
		}

		private static uint ReadUInt32(byte[] data, ref int index)
		{
			var uint32 = UnpackUInt32(data, index);
			index += sizeof(uint);
			return uint32;
		}

		private static string ReadString(byte[] data, uint size, ref int index)
		{
			var value = Encoding.UTF8.GetString(data, index, (int)size);
			index += (int)size;
			return value;
		}

		private readonly CancellationTokenSource _threadToken = new();

		private async Task ServerThread()
		{
			try
			{
				await _pipeServer.WaitForConnectionAsync(_threadToken.Token);
				do
				{
					var uintBuffer = new byte[sizeof(uint)];
					var readBytes = await _pipeServer.ReadAsync(uintBuffer, 0, uintBuffer.Length, _threadToken.Token);
					if (readBytes < uintBuffer.Length) return;
					var fullMessageSize = UnpackUInt32(uintBuffer, 0);
					var messageSize = fullMessageSize;
					var messageBytes = new byte[messageSize];
					readBytes = await _pipeServer.ReadAsync(messageBytes, 0, messageBytes.Length, _threadToken.Token);
					if (readBytes < messageBytes.Length) return;
					//process synchronously because response may be required, assumption is only one client is connected at any time
					await ProcessMessage(messageBytes);
				} while (true);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			finally
			{
				Initialized = false;
				if (_connectedProcessId.HasValue)
				{
					_mainViewModel.HookManager.RemoveProcessList(_connectedProcessId.Value);
					_connectedProcessId = null;
				}
			}
		}
		
		private async Task ProcessMessage(byte[] messageBytes)
		{
			var read = 0;
			try
			{
				var listSize = ReadUInt32(messageBytes, ref read);
				var argSizes = new List<uint>();
				var args = new List<string>();

				for (int listIndex = 0; listIndex < listSize; listIndex++)
				{
					var argSize = ReadUInt32(messageBytes, ref read);
					argSizes.Add(argSize);
				}
				for (int listIndex = 0; listIndex < listSize; listIndex++)
				{
					var arg = ReadString(messageBytes, argSizes[listIndex], ref read);
					args.Add(arg);
				}
				await ProcessMessage(args.First(), args.Skip(1).ToList());
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
		}

		private async Task ProcessMessage(string messageType, List<string> arguments)
		{
			try
			{
				switch (messageType)
				{
					case MessageType.Ping:
						var pid = int.Parse(arguments.First());
						StaticHelpers.LogToDebug($"Embed Host pinged by process : {pid}"); //todo post to console
						_connectedProcessId = pid;
						await SendSettings();
						break;
					case MessageType.Settings:
						break;
					case MessageType.EngineName:
						SetProcessEngineName(arguments.First());
						break;
					case MessageType.EngineText:
						ProcessEngineText(arguments);
						break;
					default:
						StaticHelpers.LogToFile($"Embed Host received undefined message of type '{messageType}', arguments: {string.Join(",", arguments)}");
						break;
				}
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile($"Failed to process message of type '{messageType}', arguments: {string.Join(",", arguments)}");
				StaticHelpers.LogToFile(ex);
			}
		}

		private void SetProcessEngineName(string name)
		{
			_engineName = name;
		}

		private void ProcessEngineText(List<string> arguments)
		{
			if (_mainViewModel.HookManager == null) return;
			if (!_connectedProcessId.HasValue) return;
			EngineText message;
			try
			{
				message = new EngineText(arguments);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				return;
			}
			if (!_mainViewModel.ThreadTable.Map.TryGetValue(message.Signature, out var textThread))
			{
				textThread = new EmbedTextThread(message, _engineName, _connectedProcessId.Value);
				_mainViewModel.ThreadTable.CreateThread(textThread);
				_mainViewModel.AddNewThreadToDisplayCollection(textThread);
				_mainViewModel.HookManager.SetOptionsToNewThread(textThread);
			}
			_mainViewModel.HookManager.AddTextToThread(textThread.Id, message.Text, message.Text.Length, false);
		}

		public void Dispose()
		{
			try
			{
				_threadToken.Cancel();
				if(!_listeningThread.Join(3000)) _listeningThread?.Abort();
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			finally
			{
				_pipeServer?.Dispose();
			}
		}

		/// <summary>
		/// String Enum Class
		/// </summary>
		private static class MessageType
		{
			internal const string Ping = @"agent.ping";
			internal const string Settings = @"settings";
			internal const string EngineName = @"agent.engine.name";
			internal const string EngineText = @"agent.engine.text";
		}
	}

	internal readonly struct EngineText
	{
		public string Text { get; }
		public string Hash { get; }
		public IntPtr Signature { get; }
		public TextRole Role { get; }
		public bool NeedsTranslation { get; }

		public EngineText(List<string> arguments)
		{
			Text = arguments[0];
			Hash = arguments[1];
			Signature = (IntPtr)uint.Parse(arguments[2]);
			Role = (TextRole)int.Parse(arguments[3]);
			NeedsTranslation = arguments[4] != "0";
		}
	}
}
