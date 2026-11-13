namespace RsyncZilla.Models
{
    public enum AiPermissionLevel
    {
        /// <summary>
        /// AI access is completely disabled for this session tab (Default).
        /// </summary>
        Off = 0,

        /// <summary>
        /// AI can read terminal output and remote files freely, but must ask for user confirmation in the UI before executing commands or modifying files.
        /// </summary>
        ReadAndConfirm = 1,

        /// <summary>
        /// AI has full control to read and execute commands in this terminal tab, with all actions shown live in the terminal.
        /// </summary>
        Auto = 2
    }
}

