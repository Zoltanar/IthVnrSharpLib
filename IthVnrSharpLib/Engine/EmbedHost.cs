using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace IthVnrSharpLib.Engine
{
	public class EmbedHost : IDisposable
	{
		private const string PipeName = @"vnr.socket";

		private const string PipeHostThreadName = nameof(IthVnrSharpLib) + "." + nameof(Engine) + "." + nameof(EmbedHost) + "." + nameof(ServerThread);

		private NamedPipeServerStream _pipeServer;
		private Thread _listeningThread;
		private IthVnrViewModel _mainViewModel;
		private HookManagerWrapper _hookManager;
		private ThreadTableWrapper _threadTable;
		private readonly Dictionary<(int signature, int role), EmbedTextThread> _textThreads = new();
		private readonly HashSet<int> _processesConnected = new();
		public bool Initialized { get; private set; }



		private string _engineName = "Unset";
		public void Initialize(IthVnrViewModel mainViewModel, HookManagerWrapper hookManager, ThreadTableWrapper threadTable)
		{
			_mainViewModel = mainViewModel;
			_hookManager = hookManager;
			_threadTable = threadTable;
			try
			{
				_pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);
				_listeningThread = new Thread(ServerThread) { Name = PipeHostThreadName };
				_listeningThread.Start();
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			Initialized = true;
		}

		public bool SendSettings(int pid)
		{
			if (!_pipeServer.IsConnected) return false;
			try
			{
				var data = GetDataBytes("settings", new EmbedSettings());
				var allBytes = BitConverter.GetBytes(data.Length).Reverse().Concat(data).ToArray();
				_pipeServer.Write(allBytes, 0, allBytes.Length);
				return true;
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				return false;
			}
		}

		public static void Try2()
		{
			try
			{
				var pipeClient = new NamedPipeClientStream(".", PipeName);
				pipeClient.Connect();
				var instances = pipeClient.NumberOfServerInstances;
				if (instances <= 0) return;
				var data = GetDataBytes("settings", new EmbedSettings() { embeddedTextEnabled = true });
				//var dataBytes = Encoding.UTF8.GetBytes(data);
				var allBytes = BitConverter.GetBytes(data.Length).Reverse().Concat(data).ToArray();
				pipeClient.Write(allBytes, 0, allBytes.Length);
				pipeClient.Dispose();
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			return;
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

		public class EmbedSettings
		{
			public bool embeddedScenarioTranscodingEnabled { get; set; }
			public bool embeddedFontCharSetEnabled { get; set; } = true;
			public int embeddedTranslationWaitTime { get; set; } = 2000;
			public bool embeddedOtherTranscodingEnabled { get; set; }
			public string embeddedSpacePolicyEncoding { get; set; } = string.Empty;
			public bool windowTranslationEnabled { get; set; }
			public bool windowTextVisible { get; set; } = true;
			public bool embeddedNameTranscodingEnabled { get; set; }
			public string gameEncoding { get; set; } //= "shift-jis";
			public bool embeddedOtherTranslationEnabled { get; set; }
			public bool embeddedSpaceSmartInserted { get; set; }
			public int embeddedFontCharSet { get; set; } = 0;
			public int embeddedScenarioWidth { get; set; } = 0;
			public bool embeddedScenarioTextVisible { get; set; } = true;
			public bool windowTranscodingEnabled { get; set; }
			public int nameSignature { get; set; } = 0;
			public bool embeddedScenarioTranslationEnabled { get; set; }
			public bool embeddedScenarioVisible { get; set; } = true;
			public int embeddedFontScale { get; set; }
			public bool embeddedAllTextsExtracted { get; set; }
			public bool embeddedOtherVisible { get; set; } = true;
			public string embeddedFontFamily { get; set; }
			public bool embeddedTextEnabled { get; set; } = true;
			public int scenarioSignature { get; set; } //=30661;
			public bool embeddedOtherTextVisible { get; set; } = true;
			public bool embeddedNameTextVisible { get; set; } = true;
			public bool embeddedSpaceAlwaysInserted { get; set; }
			public bool embeddedNameTranslationEnabled { get; set; }
			public bool debug { get; set; } //=true;
			public bool embeddedNameVisible { get; set; } = true;
			public int embeddedFontWeight { get; set; } = 0;
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

		private void ServerThread()
		{
			try
			{
				_pipeServer.WaitForConnection();
				do
				{
					//if (_closePipeServer) return;
					var uintBuffer = new byte[sizeof(uint)];
					var readBytes = _pipeServer.Read(uintBuffer, 0, uintBuffer.Length);
					if (readBytes < uintBuffer.Length) return;
					var fullMessageSize = UnpackUInt32(uintBuffer, 0);
					var messageSize = fullMessageSize;
					var messageBytes = new byte[messageSize];
					readBytes = _pipeServer.Read(messageBytes, 0, messageBytes.Length);
					if (readBytes < messageBytes.Length) return;
					/*Task.Run(() => */
					ProcessMessage(messageBytes) /*)*/;
				} while (true);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			finally
			{
				Initialized = false;
			}
		}

		private void ProcessMessage(byte[] messageBytes)
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
				ProcessMessage(args.First(), args.Skip(1).ToList());
				//ProcessArgs(args);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
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

		private void ProcessMessage(string messageType, List<string> arguments)
		{
			try
			{
				switch (messageType)
				{
					case MessageType.Ping:
						var pid = int.Parse(arguments.First());
						StaticHelpers.LogToDebug($"Embed Host pinged by process : {pid}");
						var added = _processesConnected.Add(pid);
						if (added) SendSettings(pid);
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
			if (_hookManager == null) return;
			var text = arguments.First();
			var threadKey = (int.Parse(arguments[2]), int.Parse(arguments[3]));
			if (!_textThreads.TryGetValue(threadKey, out var textThread))
			{
				textThread = new EmbedTextThread(threadKey, _engineName);
				_textThreads[threadKey] = textThread;
				_threadTable.SetThread((uint)textThread.Id, textThread);
				_mainViewModel.AddNewThreadToDisplayCollection(textThread);
				_hookManager.SetOptionsToNewThread(textThread);
			}
			_hookManager.ThreadOutput(textThread.Id, Encoding.Unicode.GetBytes(text), text.Length, false, IntPtr.Zero, false);
		}

		public void Dispose()
		{
			try
			{
				_listeningThread?.Abort();
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
	}


	internal class EmbedTextThread : TextThread
	{
		/// <summary>
		/// Embed Threads start here and go backwards.
		/// </summary>
		private static IntPtr TextThreadPointer = IntPtr.Subtract(IntPtr.Zero, 2);

		internal enum TextRole
		{
			UnknownRole = 0,
			ScenarioRole,
			NameRole,
			OtherRole,
			ChoiceRole = OtherRole,
			HistoryRole = OtherRole
		};

		public TextRole Role { get; }
		public override uint Status
		{
			get => 1;
			set => throw new NotSupportedException();
		}

		public EmbedTextThread((int signature, int role) key, string engineName)
		{
			var (signature, role) = key;
			Role = (TextRole)role;
			HookCode = HookNameless = HookFull = ThreadString = $"{engineName} ({signature:X}, {Role})";
			Id = IntPtr.Subtract(TextThreadPointer, (int)Role);
		}
	}
}
