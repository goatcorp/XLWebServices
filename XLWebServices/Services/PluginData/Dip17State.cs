namespace XLWebServices.Services.PluginData;

public class Dip17State
{
    public Dip17State()
    {
        this.Channels = new Dictionary<string, Channel>();
    }

    public class Channel
    {
        public Channel()
        {
            this.Plugins = new Dictionary<string, PluginState>();
        }
        
        public class PluginState
        {
            public string BuiltCommit { get; set; }
            public DateTime TimeBuilt { get; set; }
            public string EffectiveVersion { get; set; }
            
            public IDictionary<string, string> Changelogs { get; set; }
        }
        
        public IDictionary<string, PluginState> Plugins { get; set; }
    }
    
    public IDictionary<string, Channel> Channels { get; set; }
}