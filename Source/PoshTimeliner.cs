#region usings
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Posh;
using Svg;
using VVVV.Core;
using VVVV.Core.Commands;
using VVVV.Utils;

#endregion usings
namespace Timeliner
{
	public enum NudgeDirection {Back, Forward, Up, Down};
	
	public class PoshTimeliner: IDisposable
	{
		private PoshServer FPoshServer;
		private string FUrl;
		public string Url
		{
			get {return FUrl;}
		}
		private bool FDisposed = false;
		
		public Action<string> Log;
		public Action AfterHistoryPublish = () => {};
		public Action<XElement> SaveData;
		public Timeliner Timeliner;
		public TLContext Context = new TLContext();
		
		public PoshTimeliner(string url, int port)
		{
			FUrl = url;
			FPoshServer = new PoshServer(port);
			FPoshServer.AutoPublishAllAfterRemoteCall = false;
			FPoshServer.OnSessionCreated += PoshServer_SessionCreated;
			FPoshServer.OnSessionClosed += PoshServer_SessionClosed;
			FPoshServer.OnKeyDown += PoshServer_OnKeyDown;
			FPoshServer.OnKeyUp += PoshServer_OnKeyUp;
			FPoshServer.OnKeyPress += PoshServer_OnKeyPress;
			FPoshServer.OnDump += PoshServer_OnDump;
			
			Context.Initialize();
			Context.MappingRegistry.RegisterDefaultInstance<ICommandHistory>(Context.History);
			Context.MappingRegistry.RegisterDefaultInstance<PoshServer>(FPoshServer);
			Context.MappingRegistry.RegisterDefaultInstance<RemoteContext>(FPoshServer.RemoteContext);
			Context.MappingRegistry.RegisterDefaultInstance<ISvgEventCaller>(FPoshServer.SvgEventCaller);
			
			Timeliner = new Timeliner(Context);
			
			//apparently initializing the timeline with tracks adds something to the contexts but they are not flushed
			//since info goes out via initial dump. so clear them here
			FPoshServer.RemoteContext.ClearAll();
			
			//register context sending
			Timeliner.TimelineView.History.CommandInserted += HistoryChanged;
			Timeliner.TimelineView.History.Undone += HistoryChanged;
			Timeliner.TimelineView.History.Redone += HistoryChanged;
		}
		
		void HistoryChanged(object sender, EventArgs<Command> e)
		{
			Timeliner.TimelineView.RebuildAfterUpdate();
			
			//publish changes
			FPoshServer.PublishAll(this, new CallInvokedArgs(""));
		
			AfterHistoryPublish();
		}
		
		string PoshServer_OnDump()
		{
			if (Log != null)
				Log("dumping");
			var dump = Timeliner.TimelineView.SvgRoot.GetXML();
			return dump;
		}
		
		#region destructor
		// Implementing IDisposable's Dispose method.
		// Do not make this method virtual.
		// A derived class should not be able to override this method.
		public void Dispose()
		{
			Dispose(true);
			// Take yourself off the Finalization queue
			// to prevent finalization code for this object
			// from executing a second time.
			GC.SuppressFinalize(this);
		}
		
		// Dispose(bool disposing) executes in two distinct scenarios.
		// If disposing equals true, the method has been called directly
		// or indirectly by a user's code. Managed and unmanaged resources
		// can be disposed.
		// If disposing equals false, the method has been called by the
		// runtime from inside the finalizer and you should not reference
		// other objects. Only unmanaged resources can be disposed.
		protected virtual void Dispose(bool disposing)
		{
			// Check to see if Dispose has already been called.
			if(!FDisposed)
			{
				if(disposing)
				{
					Timeliner.Dispose();
					Timeliner = null;
					
					FPoshServer.Dispose();
					FPoshServer = null;
				}
				// Release unmanaged resources. If disposing is false,
				// only the following code is executed.
				
				
				// Note that this is not thread safe.
				// Another thread could start disposing the object
				// after the managed resources are disposed,
				// but before the disposed flag is set to true.
				// If thread safety is necessary, it must be
				// implemented by the client.
			}
			FDisposed = true;
		}
		
		// Use C# destructor syntax for finalization code.
		// This destructor will run only if the Dispose method
		// does not get called.
		// It gives your base class the opportunity to finalize.
		// Do not provide destructors in types derived from this class.
		~PoshTimeliner()
		{
			// Do not re-create Dispose clean-up code here.
			// Calling Dispose(false) is optimal in terms of
			// readability and maintainability.
			Dispose(false);
		}
		#endregion destructor
		
		void PoshServer_OnKeyDown(bool ctrl, bool shift, bool alt, int keyCode)
		{
			var cmd = new CompoundCommand();
			
			switch(keyCode)
			{
				case (int) Keys.Space:
					Timeliner.Timer.Play(!Timeliner.Timer.IsRunning);
					Timeliner.TimelineView.UpdateScene();
					break;
				case (int) Keys.Back:
					Timeliner.Timer.Stop();
					Timeliner.TimelineView.UpdateScene();
					break;
				case (int) Keys.Delete:
					
					foreach(var track in Timeliner.TimelineView.Tracks.OfType<ValueTrackView>())
						foreach(var kf in track.Keyframes)
					{
						if (kf.Model.Selected.Value)
							cmd.Append(Command.Remove(track.Model.Keyframes, kf.Model));
					}
					
					foreach(var track in Timeliner.TimelineView.Tracks.OfType<StringTrackView>())
						foreach(var kf in track.Keyframes)
					{
						if (kf.Model.Selected.Value)
							cmd.Append(Command.Remove(track.Model.Keyframes, kf.Model));
					}
					
					break;
				case (int) Keys.A:
					if (ctrl)
					{
						if (shift)
						{
							foreach(var track in Timeliner.TimelineView.Tracks)
								foreach(var kf in track.KeyframeViews)
									cmd.Append(Command.Set(kf.Model.Selected, true));
						}
						else
						{
							foreach(var kf in Timeliner.TimelineView.ActiveTrack.KeyframeViews)
								cmd.Append(Command.Set(kf.Model.Selected, true));
						}
					}
					break;
				case (int) Keys.Left:
					Nudge(NudgeDirection.Back, shift, ctrl, alt);
					break;
				case (int) Keys.Right:
					Nudge(NudgeDirection.Forward, shift, ctrl, alt);
					break;
				case (int) Keys.Up:
					Nudge(NudgeDirection.Up, shift, ctrl, alt);
					break;
				case (int) Keys.Down:
					Nudge(NudgeDirection.Down, shift, ctrl, alt);
					break;
				case (int) Keys.OemBackslash:
					Timeliner.TimelineView.ActiveTrack.CollapseTrack();
					break;
				case (int) Keys.I:
					cmd.Append(Command.Set(Timeliner.TimelineView.Ruler.Model.LoopStart, Timeliner.Timer.Time));
					break;
				case (int) Keys.O:
					cmd.Append(Command.Set(Timeliner.TimelineView.Ruler.Model.LoopEnd, Timeliner.Timer.Time));
					break;
				case (int) Keys.Z:
					if (ctrl)
					{
						if (shift)
						{
							Context.History.Redo();
						}
						else
						{
							Context.History.Undo();
						}
					}
					break;
			}
			
			Context.History.Insert(cmd);
		}
		
		void Nudge(NudgeDirection direction, bool shift, bool ctrl, bool alt)
		{
			var timeDelta = 1f/Timeliner.Timer.FPS;
			var valueDelta = 0.01f;
			
			if (shift)
				timeDelta *= Timeliner.Timer.FPS; //to nudge a whole second
			
			var step = alt ? 10f : 0.1f;
			if (shift) 
				valueDelta *= step;
			if (ctrl) 
				valueDelta *= step;
			
			var cmds = new CompoundCommand();
			foreach(var track in Timeliner.TimelineView.Tracks)
				track.Nudge(ref cmds, direction, timeDelta, valueDelta);
			Context.History.Insert(cmds);
		}
		
		void PoshServer_OnKeyUp(bool ctrl, bool shift, bool alt, int keyCode)
		{
			Log("keyup: " + keyCode.ToString());
		}
		
		void PoshServer_OnKeyPress(bool ctrl, bool shift, bool alt, char key)
		{
			Log("keypress: " + key);
		}
		
		void PoshServer_SessionCreated(string sessionID)
		{
			Log("session created: " + sessionID);
		}
		
		void PoshServer_SessionClosed(string sessionID)
		{
			Log("session closed: " + FPoshServer.SessionNames[sessionID]);
		}
		
		public void Save(string path)
		{
			FUrl = Path.GetFileNameWithoutExtension(path);
			Timeliner.Save(path);
		}
		
		public void Load(string path)
		{
			var xml = XElement.Load(path);
			Timeliner.Load(xml);
			Timeliner.TimelineView.UpdateScene();
		}
		
		public void Evaluate(float hosttime)
		{
			Timeliner.Evaluate(hosttime);
			
			//here we can only publish updates (no adds) since those start pulling status from the scenegraph while user-action can insert at the same time
			FPoshServer.PublishUpdate();
			FPoshServer.PublishContent();
		}
	}
}