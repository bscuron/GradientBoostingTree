// TODO: error handling
#region Using declarations
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Text;
using System.Linq;
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

		private string CONFIGURATION_PATH = System.Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents\projects\GradientBoostingTree\config.json");
		private Dictionary<string, object> CONFIGURATION;
		private Dictionary<string, object> CONFIGURATION_TCP;
		private Dictionary<string, object> CONFIGURATION_PAYLOAD_TYPE;
		private volatile bool TrainingFinished = false;
		private TcpClient client;
		private Thread requestThread;

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
					MaximumBarsLookBack							= MaximumBarsLookBack.Infinite;
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
					TrainingSamples = 250_000;
					TrainingSamplesOffset = 0;
					SaveModel = true;
					break;
				case State.Configure:
					BarsPeriod primaryBarsPeriod = new BarsPeriod {BarsPeriodType = BarsPeriod.BarsPeriodType, Value = BarsPeriod.Value};
					AddDataSeries(BarsPeriod.BarsPeriodType, BarsPeriod.Value);
					break;
				case State.Active: break;
				case State.DataLoaded:
					CONFIGURATION = LoadConfiguration(CONFIGURATION_PATH);
					CONFIGURATION_TCP = CONFIGURATION["TCP"] as Dictionary<string, object>;
					CONFIGURATION_PAYLOAD_TYPE = CONFIGURATION["PAYLOAD_TYPE"] as Dictionary<string, object>;
					client = Connect(Convert.ToString(CONFIGURATION_TCP["HOST"]), Convert.ToInt32(CONFIGURATION_TCP["PORT"]));

					if (TrainModel)
					{
						requestThread = new Thread(RequestData);
						requestThread.IsBackground = true;
						requestThread.Start();
					}
					break;
				case State.Historical:
					break;
				case State.Transition: break;
				case State.Realtime: break;
				case State.Terminated:
					Disconnect(client);
					break;
			}
		}

		private void RequestData()
		{
			BarsRequest request = new BarsRequest(BarsArray[1].Instrument, TrainingSamples + TrainingSamplesOffset);
			request.BarsPeriod = new BarsPeriod {BarsPeriodType = BarsPeriod.BarsPeriodType, Value = BarsPeriod.Value};
			request.TradingHours = Bars.Instrument.MasterInstrument.TradingHours;
			request.Request(new Action<BarsRequest, ErrorCode, string>((bars, errorCode, errorMessage) => {
				int n = bars.Bars.Count - TrainingSamplesOffset;
				for (int i = 0; i < n; i++)
				{
					var row = new {
						Type = Convert.ToInt32(CONFIGURATION_PAYLOAD_TYPE["TRAIN_ROW"]),
						Time =  ((DateTimeOffset)bars.Bars.GetTime(i)).ToUnixTimeMilliseconds(),
						Volume = bars.Bars.GetVolume(i),
						Open = bars.Bars.GetOpen(i),
						High = bars.Bars.GetHigh(i),
						Low = bars.Bars.GetLow(i),
						Close = bars.Bars.GetClose(i),
					};
					Send(client, row);
				}
				Send(client, new { Type = Convert.ToInt32(CONFIGURATION_PAYLOAD_TYPE["TRAIN_START"]), Save = SaveModel });

				Dictionary<string, object> response = Receive<Dictionary<string, object>>(client);
				if (Convert.ToInt32(response["Type"]) == Convert.ToInt32(CONFIGURATION_PAYLOAD_TYPE["TRAIN_FINISH"]))
				{
					TrainingFinished = true;
					Print("[INFO] Model trained");
				}
			}));

		}

		protected override void OnBarUpdate()
		{
			while ((TrainModel && !TrainingFinished) || client == null)
			{
				Thread.Sleep(1000);
			}
			var row = new {
				Type = Convert.ToInt32(CONFIGURATION_PAYLOAD_TYPE["ROW"]),
				Time =  ((DateTimeOffset)Time[0]).ToUnixTimeMilliseconds(),
				Volume = Volume[0],
				Open = Open[0],
				High = High[0],
				Low = Low[0],
				Close = Close[0]
			};
			Send(client, row);
			Dictionary<string, object> response = Receive<Dictionary<string, object>>(client);
			if (Convert.ToInt32(response["Type"]) == Convert.ToInt32(CONFIGURATION_PAYLOAD_TYPE["CLASS"]))
			{
				int predictionClass = Convert.ToInt32(response["Class"]);
				if (predictionClass == 1)
				{
					Draw.ArrowDown(this, CurrentBar.ToString(), true, 0, High[0] + TickSize * 4, Brushes.Yellow);
				}
				else if (predictionClass == 2)
				{
					Draw.ArrowUp(this, CurrentBar.ToString(), true, 0, Low[0] - TickSize * 4, Brushes.Yellow);
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
			Dictionary<string, object> configuration = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
			return configuration;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="TrainModel", Order=1, GroupName="Parameters")]
		public bool TrainModel
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TrainingSamples", Order=2, GroupName="Parameters")]
		public int TrainingSamples { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="TrainingSamplesOffset", Order=2, GroupName="Parameters")]
		public int TrainingSamplesOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Save Model", Order=3, GroupName="Parameters")]
		public bool SaveModel
		{ get; set; }
		#endregion
	}
}
