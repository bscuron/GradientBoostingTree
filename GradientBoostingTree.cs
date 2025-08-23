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

		private string CONFIG_PATH = System.Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents\projects\GradientBoostingTree\config.json");
		private bool TrainingStarted = false;
		private Dictionary<string, object> CONFIG;
		private Dictionary<string, object> TCP;
		private Dictionary<string, object> PAYLOAD_TYPE;
		private int HISTORICAL_BARS_COUNT;
		private TcpClient client;
		// private Indicators._The_Indicator_Store.TIS_PRC_v2c prc;
		private WilliamsR williamsR;
		private SMA sma10;
		private SMA sma20;
		private SMA sma50;

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
					BarsRequiredToTrade							= 26;
					IsInstantiatedOnEachOptimizationIteration	= true;
					TrainModel = true;
					SaveModel = true;
					break;
				case State.Configure:
					// prc = TIS_PRC_v2c(3, 100, 1.62, 2);
					williamsR = WilliamsR(14);
					sma10 = SMA(10);
					sma20 = SMA(20);
					sma50 = SMA(50);
					break;
				case State.Active: break;
				case State.DataLoaded:
					CONFIG = LoadConfiguration(CONFIG_PATH);
					TCP = CONFIG["TCP"] as Dictionary<string, object>;
					PAYLOAD_TYPE = CONFIG["PAYLOAD_TYPE"] as Dictionary<string, object>;
					client = Connect(Convert.ToString(TCP["HOST"]), Convert.ToInt32(TCP["PORT"]));
					break;
				case State.Historical:
					HISTORICAL_BARS_COUNT = Bars.Count;
					break;
				case State.Transition: break;
				case State.Realtime: break;
				case State.Terminated:
					Disconnect(client);
					break;
			}
		}

		protected override void OnBarUpdate()
		{
			if (client == null) return;
			if (CurrentBar < BarsRequiredToTrade) return;
			if (TrainModel && CurrentBar == (int)(TrainingPercentage * HISTORICAL_BARS_COUNT))
			{
				TrainingStarted = true;
				Print("[INFO] Message sent to server to begin training");
				Send(client, new { type = Convert.ToInt32(PAYLOAD_TYPE["TRAIN_START"]), save = SaveModel });
				Draw.VerticalLine(this, CurrentBar.ToString(), 0, Brushes.Cyan);
				Dictionary<string, object> response = Receive<Dictionary<string, object>>(client);
				int type = Convert.ToInt32(response["type"]);
				if (type == Convert.ToInt32(PAYLOAD_TYPE["TRAIN_END"]))
				Print("[INFO] Model trained");
			}
			if (TrainingStarted) return;

			double eps = 1e-6;
			double candle_body = Close[0] - Open[0] + eps;
			double candle_body_size = Math.Abs(candle_body);
			double candle_upperwick = High[0] - Math.Max(Open[0], Close[0]);
			double candle_lowerwick = Math.Min(Open[0], Close[0]) - Low[0] + eps;
			double candle_range = High[0] - Low[0] + eps;
			double candle_direction = Math.Sign(candle_body);
			var message = new {
				type = Convert.ToInt32(PAYLOAD_TYPE[TrainModel ? "TRAIN_DATA" : "DATA"]),
				time = ((DateTimeOffset)Time[0]).ToUnixTimeMilliseconds(),
				high = High[0],
				low = Low[0],
				close = Close[0],

				candle_body_size = candle_body_size,
				candle_range = candle_range,
				candle_body_to_range_ratio = candle_body_size / candle_range,
				candle_upperwick = candle_upperwick,
				candle_lowerwick = candle_lowerwick,
				candle_body_to_wick_ratio = candle_body_size / (candle_upperwick + candle_lowerwick),
				candle_body_to_volume_ratio = candle_body_size / Volume[0],
				candle_wick_ratio = candle_upperwick / candle_lowerwick,
				candle_wick_to_range_ratio = (candle_upperwick + candle_lowerwick) / candle_range,
				candle_upperwick_to_range_ratio = candle_upperwick / candle_range,
				candle_lowerwick_to_range_ratio = candle_lowerwick / candle_range,
				candle_wick_to_volume_ratio = (candle_upperwick + candle_lowerwick) / Volume[0],
				candle_upperwick_to_volume_ratio = candle_upperwick / Volume[0],
				candle_lowerwick_to_volume_ratio = candle_lowerwick / Volume[0],

				// indicator_prc_fx = Close[0] - prc.Fx[0],
				// indicator_prc_sqh = Close[0] - prc.Sqh[0],
				// indicator_prc_sqh2 = Close[0] - prc.Sqh2[0],
				// indicator_prc_sql = Close[0] - prc.Sql[0],
				// indicator_prc_sql2 = Close[0] - prc.Sql2[0],

				indicator_williamsR = williamsR[0],

				indicator_sma10 = Close[0] - sma10[0],
				indicator_sma20 = Close[0] - sma20[0],
				indicator_sma50 = Close[0] - sma50[0]
			};
			// Print($"[INFO] Sending message: {message}");
			Send(client, message);

			if (!TrainModel)
			{
				Dictionary<string, object> response = Receive<Dictionary<string, object>>(client);
				int type = Convert.ToInt32(response["type"]);
				if (type == Convert.ToInt32(PAYLOAD_TYPE["PRED"]))
				{
					int pred = Convert.ToInt32(response["class"]);
					Print($"[INFO] Prediction: {pred}");
					if (pred == 1)
					{
						Draw.ArrowDown(this, CurrentBar.ToString(), true, 0, High[0] + TickSize * 2, Brushes.White);
					}
					else if (pred == 2)
					{
						Draw.ArrowUp(this, CurrentBar.ToString(), true, 0, Low[0] - TickSize * 2, Brushes.White);
					}
				}
			}
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
		#region Properties
		[NinjaScriptProperty]
		[Display(Name="TrainModel", Order=1, GroupName="Parameters")]
		public bool TrainModel
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 0.95)]
		[Display(Name="TrainingPercentage", Order=2, GroupName="Parameters")]
		public double TrainingPercentage { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Save Model", Order=3, GroupName="Parameters")]
		public bool SaveModel
		{ get; set; }
		#endregion
	}
}
