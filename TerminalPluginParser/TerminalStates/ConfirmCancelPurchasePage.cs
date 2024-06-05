﻿using LethalInternship.Enums;
using LethalInternship.Managers;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalInternship.TerminalPluginParser.TerminalStates
{
    internal class ConfirmCancelPurchasePage : TerminalState
    {
        private static readonly EnumTerminalStates STATE = EnumTerminalStates.ConfirmCancelPurchase;
        public override EnumTerminalStates GetTerminalState() { return STATE; }

        private int NbOrdered;

        public ConfirmCancelPurchasePage(TerminalState newState, int nbOrdered) : base(newState)
        {
            int maxOrder = (int)Math.Floor((float)TerminalManager.Instance.GetTerminal().groupCredits / (float)Const.PRICE_INTERN);
            this.NbOrdered = nbOrdered < maxOrder ? nbOrdered : maxOrder;
        }

        public override bool ParseCommandValid(string[] words)
        {
            string firstWord = words[0];
            if (string.IsNullOrWhiteSpace(firstWord))
            {
                return false;
            }

            if (Const.STRING_INTERNSHIP_PROGRAM_COMMAND.Contains(firstWord)
                || Const.STRING_CANCEL_COMMAND.Contains(firstWord)
                || Const.STRING_BACK_COMMAND.Contains(firstWord))
            {
                // get back to info page
                terminalParser.TerminalState = new InfoPage(this);
                return true;
            }

            if (Const.STRING_CONFIRM_COMMAND.Contains(firstWord))
            {
                // Confirm
                int cost = TerminalManager.Instance.GetTerminal().groupCredits - (Const.PRICE_INTERN * this.NbOrdered);
                TerminalManager.Instance.SyncPurchaseAndCredits(this.NbOrdered, cost);
                InternManager.NbInternsToDropShip = terminalParser.NbInternsAlreadyBought;
                Plugin.Logger.LogDebug($"NbInternsToDropShip {InternManager.NbInternsToDropShip}");

                terminalParser.TerminalState = new InfoPage(this);
                return true;
            }

            return false;
        }

        public override TerminalNode? DisplayNode()
        {
            if (!dictTerminalNodeByState.TryGetValue(this.GetTerminalState(), out TerminalNode terminalNode))
            {
                terminalNode = ScriptableObject.CreateInstance<TerminalNode>();
                dictTerminalNodeByState[this.GetTerminalState()] = terminalNode;
            }
            terminalNode.clearPreviousText = true;

            int internsAvailable = Const.INTERN_AVAILABLE - terminalParser.NbInternsAlreadyBought;
            string invalidNumber = string.Empty;
            if (this.NbOrdered > internsAvailable)
            {
                invalidNumber = Const.TEXT_CONFIRM_CANCEL_PURCHASE_MAXIMUM;
                this.NbOrdered = internsAvailable;
            }

            int totalNbInternsOnNextMoon = this.NbOrdered + terminalParser.NbInternsAlreadyBought;
            terminalNode.displayText = string.Format(Const.TEXT_CONFIRM_CANCEL_PURCHASE, invalidNumber, this.NbOrdered, totalNbInternsOnNextMoon);

            return terminalNode;
        }
    }
}
