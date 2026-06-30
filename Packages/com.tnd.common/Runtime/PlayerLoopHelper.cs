using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace TND.Common
{
    public static class PlayerLoopHelper
    {
        public static void InsertPlayerLoopSystem<TPlayerLoop, TInjectAfter>(PlayerLoopSystem.UpdateFunction updateDelegate)
            where TInjectAfter : struct
        {
            PlayerLoopSystem currentLoop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem newPlayerLoop = new()
            {
                subSystemList = null,
                updateDelegate = updateDelegate,
                type = typeof(TPlayerLoop),
            };

            PlayerLoopSystem updatedLoop = InsertPlayerLoopSystem<TInjectAfter>(currentLoop, newPlayerLoop);
            PlayerLoop.SetPlayerLoop(updatedLoop);
        }
        
        public static PlayerLoopSystem InsertPlayerLoopSystem<TInjectAfter>(in PlayerLoopSystem loopSystem, PlayerLoopSystem newSystem) 
            where TInjectAfter : struct
        {
            // Create a new root PlayerLoopSystem
            PlayerLoopSystem newPlayerLoop = new()
            {
                loopConditionFunction = loopSystem.loopConditionFunction,
                type = loopSystem.type,
                updateDelegate = loopSystem.updateDelegate,
                updateFunction = loopSystem.updateFunction
            };
            
            // Create a new list to populate with subsystems, including the custom system
            List<PlayerLoopSystem> newSubSystemList = new();

            // Iterate through the subsystems in the existing loop we passed in and add them to the new list
            if (loopSystem.subSystemList != null)
            {
                for (var i = 0; i < loopSystem.subSystemList.Length; i++)
                {
                    newSubSystemList.Add(loopSystem.subSystemList[i]);
                    // If the previously added subsystem is of the type to add after, add the custom system
                    if (loopSystem.subSystemList[i].type == typeof(TInjectAfter))
                    {
                        newSubSystemList.Add(newSystem);
                    }
                }
            }

            newPlayerLoop.subSystemList = newSubSystemList.ToArray();
            return newPlayerLoop;
        }
    }
}
