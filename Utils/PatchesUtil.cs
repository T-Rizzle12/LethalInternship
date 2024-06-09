﻿using GameNetcodeStuff;
using HarmonyLib;
using LethalInternship.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalInternship.Utils
{
    internal static class PatchesUtil
    {
        public static readonly MethodInfo AreInternsScheduledToLandMethod = SymbolExtensions.GetMethodInfo(() => AreInternsScheduledToLand());
        public static readonly MethodInfo IsPlayerLocalOrInternOwnerLocalMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLocalOrInternOwnerLocal(new PlayerControllerB()));
        public static readonly MethodInfo IsColliderFromLocalOrInternOwnerLocalMethod = SymbolExtensions.GetMethodInfo(() => IsColliderFromLocalOrInternOwnerLocal(new Collider()));
        public static readonly MethodInfo IsObjectHeldByInternMethodInfo = SymbolExtensions.GetMethodInfo(() => IsObjectHeldByIntern((GrabbableObject)new object()));
        public static readonly MethodInfo IndexBeginOfInternsMethod = SymbolExtensions.GetMethodInfo(() => IndexBeginOfInterns());

        private static readonly MethodInfo IsPlayerInternMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerIntern(new PlayerControllerB()));

        public static List<CodeInstruction> InsertIsPlayerInternInstructions(List<CodeInstruction> codes,
                                                                             ILGenerator generator,
                                                                             int startIndex,
                                                                             int indexToJumpTo)
        {
            Label labelToJumpTo;
            List<Label> labelsOfStartCode = codes[startIndex].labels;
            List<Label> labelsOfCodeToJumpTo = codes[startIndex + indexToJumpTo].labels;
            List<CodeInstruction> codesToAdd;

            // Define label for the jump
            labelToJumpTo = generator.DefineLabel();
            labelsOfCodeToJumpTo.Add(labelToJumpTo);

            // Rearrange label if start is a destination label for a previous code
            if (labelsOfStartCode.Count > 0)
            {
                codes.Insert(startIndex + 1, new CodeInstruction(codes[startIndex].opcode, codes[startIndex].operand));
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                startIndex++;
            }

            codesToAdd = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, IsPlayerInternMethod),
                new CodeInstruction(OpCodes.Brtrue_S, labelToJumpTo)
            };
            codes.InsertRange(startIndex, codesToAdd);
            return codes;
        }

        public static List<CodeInstruction> InsertLogOfFieldOfThis(string logWithZeroParameter, FieldInfo fieldInfo, Type fieldType)
        {
            return new List<CodeInstruction>()
                {
                    new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Plugin), "Logger")),
                    new CodeInstruction(OpCodes.Ldstr, logWithZeroParameter),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, fieldInfo),
                    new CodeInstruction(OpCodes.Box, fieldType),
                    new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => String.Format(new String(new char[]{ }), new object()))),
                    new CodeInstruction(OpCodes.Callvirt, SymbolExtensions.GetMethodInfo(() => Plugin.Logger.LogDebug(new object()))),
                };

            //codes.InsertRange(0, PatchesUtil.InsertLogOfFieldOfThis("isPlayerControlled {0}", AccessTools.Field(typeof(PlayerControllerB), "isPlayerControlled"), typeof(bool)));
        }

        public static List<CodeInstruction> InsertLogWithoutParameters(string log)
        {
            return new List<CodeInstruction>()
                {
                    new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Plugin), "Logger")),
                    new CodeInstruction(OpCodes.Ldstr, log),
                    new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => String.Format(new String(new char[]{ })))),
                    new CodeInstruction(OpCodes.Callvirt, SymbolExtensions.GetMethodInfo(() => Plugin.Logger.LogDebug(new object()))),
                };

            //codes.InsertRange(0, PatchesUtil.InsertLogOfFieldOfThis("isPlayerControlled {0}", AccessTools.Field(typeof(PlayerControllerB), "isPlayerControlled"), typeof(bool)));
        }

        private static bool AreInternsScheduledToLand()
        {
            return InternManager.Instance.AreInternsScheduledToLand();
        }
        private static bool IsPlayerLocalOrInternOwnerLocal(PlayerControllerB player)
        {
            return InternManager.Instance.IsPlayerLocalOrInternOwnerLocal(player);
        }
        private static bool IsObjectHeldByIntern(GrabbableObject grabbableObject)
        {
            return InternManager.Instance.IsObjectHeldByIntern(grabbableObject);
        }
        private static int IndexBeginOfInterns()
        {
            return InternManager.Instance.IndexBeginOfInterns;
        }
        private static bool IsColliderFromLocalOrInternOwnerLocal(Collider collider)
        {
            return InternManager.Instance.IsColliderFromLocalOrInternOwnerLocal(collider);
        }
        private static bool IsPlayerIntern(PlayerControllerB player)
        {
            return InternManager.Instance.IsPlayerIntern(player);
        }
        private static bool IsPlayerInternOwnerLocal(PlayerControllerB player)
        {
            return InternManager.Instance.IsPlayerInternOwnerLocal(player);
        }
    }
}
