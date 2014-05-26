#region usings
using System;
using System.IO;
using System.Windows.Forms;
using System.Xml.Linq;

using Svg;
using VVVV.Core;
using VVVV.Core.Commands;
using VVVV.Utils;
using Posh;

#endregion usings
namespace Timeliner
{
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
        public Action Changed;
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
            
            //todo: remove init hack:
            var trackOrder = 0;
            TLValueTrack t;
            TLKeyframe k = null;
            Command cmd;
            var rnd = new Random();
            for (int i = 0; i < 0; i++)
            {
                t = new TLValueTrack();
                t.Loading = true;
                t.Order.Value = trackOrder++;
                cmd = Command.Add(Timeliner.TimelineModel.Tracks, t);
                Timeliner.TimelineView.History.Insert(cmd);
            	
                for (int j = 0; j < 100; j++)
                {
                    k = new TLKeyframe(j * 10, (float) (rnd.NextDouble() - 0.5));
                    cmd = Command.Add(t.Keyframes, k);
                    Timeliner.TimelineView.History.Insert(cmd);
                }
                t.Loading = false;
            }
            
            TLAudioTrack at;
            TLSample s = null;
            for (int i = 0; i < 0; i++)
            {
                at = new TLAudioTrack();
                at.Loading = true;
                at.LoadFile();
                at.Order.Value = trackOrder++;
                cmd = Command.Add(Timeliner.TimelineModel.Tracks, at);
                Timeliner.TimelineView.History.Insert(cmd);
                
                at.Loading = false;
            }
            
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
            //publish changes
            FPoshServer.PublishAll(this, new CallInvokedArgs(""));
            
            if (Changed != null)
            	Changed();
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
            switch(keyCode)
            {
                case (int) Keys.Space:
                    Timeliner.Timer.IsRunning = !Timeliner.Timer.IsRunning;
                    break;
                case (int) Keys.Z:
                    if (ctrl)
                    if (shift)
                    {
                        Context.History.Redo();
                        //HACK to build curves
                        foreach (var track in Timeliner.TimelineModel.Tracks) 
                        {
                            if (track is TLValueTrack)
                                (track as TLValueTrack).BuildCurves();
                        }
                        
                    }
                    else
                    {
                        Context.History.Undo();
                        //HACK to build curves
                        foreach (var track in Timeliner.TimelineModel.Tracks) 
                        {
                            if (track is TLValueTrack)
                                (track as TLValueTrack).BuildCurves();
                        }
                    }
                    break;
        }
			
            Log("keydown: " + keyCode.ToString());
        }
		
        void PoshServer_OnKeyUp(bool ctrl, bool shift, bool alt, int keyCode)
        {
            Log("keyup: " + keyCode.ToString());
        }
		
        void PoshServer_OnKeyPress(bool ctrl, bool shift, bool alt, char key)
        {
            Log("keypress: " + key);
			
            switch(key)
            {
                //save current document
                case 's':
                    var path = Path.Combine(WebServer.TerminalPath, FUrl) + ".xml";
                    Save(path);
                    break;
        }
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