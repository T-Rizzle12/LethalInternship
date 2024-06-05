﻿using LethalInternship.AI;
using LethalInternship.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalInternship.TerminalPluginParser
{
    internal abstract class TerminalState
    {
        protected TerminalParser terminalParser;
        protected Dictionary<EnumTerminalStates, TerminalNode> dictTerminalNodeByState;

        protected TerminalState(TerminalState newState) : this(newState.terminalParser)
        {
            this.dictTerminalNodeByState = newState.dictTerminalNodeByState;
        }

        protected TerminalState(TerminalParser terminalParser)
        {
            if (terminalParser == null)
            {
                throw new System.Exception("TerminalParser is null.");
            }

            this.terminalParser = terminalParser;
            Plugin.Logger.LogDebug($"TerminalState new state :                 {this.GetTerminalState()}");

            if (this.dictTerminalNodeByState == null)
            {
                dictTerminalNodeByState = new Dictionary<EnumTerminalStates, TerminalNode>();
            }
        }
        public abstract EnumTerminalStates GetTerminalState();

        public abstract bool ParseCommandValid(string[] words);

        public abstract TerminalNode? DisplayNode();
    }
}
