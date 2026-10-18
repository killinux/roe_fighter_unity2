using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Nothing but a line in the log: a batch run of it tells whether the project's scripts compile.
    ///   .\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeCompileCheck.Run
    /// </summary>
    public static class RoeCompileCheck
    {
        public static void Run()
        {
            Debug.Log("[ROE] compiled: the project's scripts build; nothing else was done");
        }
    }
}
