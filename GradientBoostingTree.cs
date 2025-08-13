// TODO: error handling
#region Using declarations
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class GradientBoostingTree : Strategy
	{

		private const string CONFIG_PATH = @"C:\Users\bscur\Documents\projects\GradientBoostingTree\config.json";
		private Dictionary<string, object> CONFIG;
		private Dictionary<string, object> TCP;
		private Dictionary<string, object> PAYLOAD_TYPE;
		private TcpClient client;
		private WilliamsR williamsR14;
		private ROC roc14;
		private VROC vroc14;
		private ATR atr14;

		protected override void OnStateChange()
		{
			switch (State)
			{

				case State.SetDefaults:
					Description									= @"Enter the description for your new custom Strategy here.";
					Name										= "Gradient Boosting Tree";
					Calculate									= Calculate.OnBarClose;
					EntriesPerDirection							= 1;
					EntryHandling								= EntryHandling.AllEntries;
					IsExitOnSessionCloseStrategy				= true;
					ExitOnSessionCloseSeconds					= 30;
					IsFillLimitOnTouch							= false;
					MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
					OrderFillResolution							= OrderFillResolution.Standard;
					Slippage									= 0;
					StartBehavior								= StartBehavior.WaitUntilFlat;
					TimeInForce									= TimeInForce.Gtc;
					TraceOrders									= false;
					RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
					StopTargetHandling							= StopTargetHandling.PerEntryExecution;
					BarsRequiredToTrade							= 200;
					IsInstantiatedOnEachOptimizationIteration	= true;
					break;
				case State.Configure:
					williamsR14 = WilliamsR(14);
					roc14 = ROC(14);
					vroc14 = VROC(14, 3);
					atr14 = ATR(14);
					break;
				case State.Active: break;
				case State.DataLoaded:
					CONFIG = LoadConfiguration(CONFIG_PATH);
					TCP = CONFIG["TCP"] as Dictionary<string, object>;
					PAYLOAD_TYPE = CONFIG["PAYLOAD_TYPE"] as Dictionary<string, object>;
					client = Connect(Convert.ToString(TCP["HOST"]), Convert.ToInt32(TCP["PORT"]));
					break;
				case State.Historical: break;
				case State.Transition: break;
				case State.Realtime: break;
				case State.Terminated:
					Disconnect(client);
					break;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade) return;
			if (client == null) return;
			if (CurrentBar == Bars.Count - 2) // TODO: confirm this is correct
			{
				Print("[INFO] Message sent to server to begin training");
				Send(client, new { type = Convert.ToInt32(PAYLOAD_TYPE["TRAIN"]) });
			}

			var message = new {
				type = Convert.ToInt32(PAYLOAD_TYPE["ROW"]),
				time = ((DateTimeOffset)Time[0]).ToUnixTimeMilliseconds(),
				high = High[0],
				low = Low[0],
				body = Math.Abs(Close[0] - Open[0]),
				upperwick = High[0] - Math.Max(Open[0], Close[0]),
				lowerwick = Math.Min(Open[0], Close[0]) - Low[0],
				volume = Volume[0],
				williamsR14 = williamsR14[0],
				roc14 = roc14[0],
				vroc14 = vroc14[0],
				atr14 = atr14[0],
			};
			Send(client, message);
			Print($"[INFO] Message sent to server: {message}");

			Dictionary<string, object> response = Receive<Dictionary<string, object>>(client);
			Print("[INFO] Server response: " + Convert.ToInt32(response["type"]));
		}

		private TcpClient Connect(string host, int port)
		{
			Print("[INFO] Connecting to server...");
			try
			{
				TcpClient client = new TcpClient(host, port);
				Print("[INFO] Connected to server");
				return client;
			}
			catch (Exception e)
			{
				Print($"[ERROR] Failed to connect to server: {e.Message}");
				return Connect(host, port);
			}
		}

		private void Disconnect(TcpClient client)
		{
			if (client == null) return;
			try
			{
				client.Close();
				client.Dispose();
				Print("[INFO] Disconnected from server");
			}
			catch (Exception e)
			{
				Print($"[ERROR] Failed to disconnect from server: {e.Message}");
			}
			client = null;
		}

		private void Send(TcpClient client, object payload)
		{
			JavaScriptSerializer serializer = new JavaScriptSerializer();
			byte[] data = Encoding.UTF8.GetBytes(serializer.Serialize(payload));

			NetworkStream stream = client.GetStream();
			byte[] sizeBytes = BitConverter.GetBytes(data.Length);
			if (BitConverter.IsLittleEndian)
				Array.Reverse(sizeBytes);

			stream.Write(sizeBytes, 0, sizeBytes.Length);
			stream.Write(data, 0, data.Length);
			stream.Flush();
		}

		public static T Receive<T>(TcpClient client)
		{
			NetworkStream stream = client.GetStream();
			byte[] RecvAll(int n)
			{
				byte[] buffer = new byte[n];
				int offset = 0;
				while (offset < n)
				{
					int bytes = stream.Read(buffer, offset, n - offset);
					if (bytes == 0)
					{
						// TODO: try to reconnect
						throw new Exception("[ERROR] Disconnected from TCP server");
					}
					offset += bytes;
				}
				return buffer;
			}

			byte[] headerBytes = RecvAll(4);
			if (BitConverter.IsLittleEndian)
				Array.Reverse(headerBytes);

			int payloadSize = BitConverter.ToInt32(headerBytes, 0);
			byte[] payloadBytes = RecvAll(payloadSize);
			string json = Encoding.UTF8.GetString(payloadBytes);

			JavaScriptSerializer serializer = new JavaScriptSerializer();
			return serializer.Deserialize<T>(json);
		}

		private Dictionary<string, object> LoadConfiguration(String path)
		{
			JavaScriptSerializer serializer = new JavaScriptSerializer();
			Dictionary<string, object> config = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
			return config;
		}
	}
}
