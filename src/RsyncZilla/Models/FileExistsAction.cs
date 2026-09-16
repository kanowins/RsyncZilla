namespace RsyncZilla.Models
{
    public enum FileExistsAction
    {
        /// <summary>
        /// Default: Updates destination if size or modification date differ (standard rsync -avzP without --update).
        /// </summary>
        OverwriteIfDifferent = 0,

        /// <summary>
        /// Updates destination only if source file is newer than destination (rsync -avzP --update).
        /// </summary>
        OverwriteIfNewer = 1,

        /// <summary>
        /// Always forces delta transfer regardless of modification times or sizes (rsync -avzP --ignore-times).
        /// </summary>
        OverwriteAlways = 2,

        /// <summary>
        /// Compares file contents by 128-bit checksum instead of timestamps (rsync -avzP --checksum).
        /// </summary>
        CompareChecksum = 3
    }
}
