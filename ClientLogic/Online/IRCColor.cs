using ClientLogic.UI;

namespace DTAClient.Online
{
    public class IRCColor
    {
        public IRCColor(string name, bool selectable, ChatColor color, int ircColorId)
        {
            Name = name;
            Selectable = selectable;
            Color = color;
            IrcColorId = ircColorId;
        }

        public string Name { get; private set; }
        public bool Selectable { get; private set; }
        public ChatColor Color { get; private set; }
        public int IrcColorId { get; private set; }
    }
}
