﻿using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveGoTo : AIObjective
    {
        public override string DebugTag => "go to";

        private AIObjectiveFindDivingGear findDivingGear;
        private bool repeat;
        //how long until the path to the target is declared unreachable
        private float waitUntilPathUnreachable;
        private bool getDivingGearIfNeeded;

        public Func<bool> customCondition;

        /// <summary>
        /// Display units
        /// </summary>
        public float CloseEnough { get; set; } = 50;
        public bool IgnoreIfTargetDead { get; set; }
        public bool AllowGoingOutside { get; set; }
        public bool CheckVisibility { get; set; }

        public override float GetPriority()
        {
            if (FollowControlledCharacter && Character.Controlled == null) { return 0.0f; }
            if (Target is Entity e && e.Removed) { return 0.0f; }
            if (IgnoreIfTargetDead && Target is Character character && character.IsDead) { return 0.0f; }                     
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return 1.0f;
        }

        public ISpatialEntity Target { get; private set; }

        public bool FollowControlledCharacter;

        public AIObjectiveGoTo(ISpatialEntity target, Character character, AIObjectiveManager objectiveManager, bool repeat = false, bool getDivingGearIfNeeded = true, float priorityModifier = 1) 
            : base (character, objectiveManager, priorityModifier)
        {
            this.Target = target;
            this.repeat = repeat;
            waitUntilPathUnreachable = 2.0f;
            this.getDivingGearIfNeeded = getDivingGearIfNeeded;
            CalculateCloseEnough();
        }

        protected override void Act(float deltaTime)
        {
            if (FollowControlledCharacter)
            {
                if (Character.Controlled == null)
                {
                    abandon = true;
                    return;
                }
                Target = Character.Controlled;
            }
            if (Target == character)
            {
                character.AIController.SteeringManager.Reset();
                abandon = true;
                return;
            }           
            waitUntilPathUnreachable -= deltaTime;
            if (!character.IsClimbing)
            {
                character.SelectedConstruction = null;
            }
            if (Target is Entity e)
            {
                if (e.Removed)
                {
                    abandon = true;
                }
                else
                {
                    character.AIController.SelectTarget(e.AiTarget);
                }
            }
            bool isInside = character.CurrentHull != null;
            bool insideSteering = SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.IsPathDirty;
            var targetHull = Target is Hull h ? h : Target is Item i ? i.CurrentHull : Target is Character c ? c.CurrentHull : character.CurrentHull;
            bool targetIsOutside = (Target != null && targetHull == null) || (insideSteering && PathSteering.CurrentPath.HasOutdoorsNodes);
            if (isInside && targetIsOutside && !AllowGoingOutside)
            {
                abandon = true;
            }
            else if (!repeat && waitUntilPathUnreachable < 0)
            {
                if (SteeringManager == PathSteering && PathSteering.CurrentPath != null)
                {
                    abandon = PathSteering.CurrentPath.Unreachable;
                }
            }
            if (abandon)
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Cannot reach the target: {Target.ToString()}", Color.Yellow);
#endif
                if (objectiveManager.CurrentOrder != null)
                {
                    character.Speak(TextManager.Get("DialogCannotReach"), identifier: "cannotreach", minDurationBetweenSimilar: 10.0f);
                }
                character.AIController.SteeringManager.Reset();
            }
            else
            {
                Vector2 currTargetSimPos = Vector2.Zero;
                currTargetSimPos = Target.SimPosition;
                // Take the sub position into account in the sim pos
                if (SteeringManager != PathSteering && character.Submarine == null && Target.Submarine != null)
                {
                    currTargetSimPos += Target.Submarine.SimPosition;
                }
                else if (character.Submarine != null && Target.Submarine == null)
                {
                    currTargetSimPos -= character.Submarine.SimPosition;
                }
                else if (character.Submarine != Target.Submarine)
                {
                    if (character.Submarine != null && Target.Submarine != null)
                    {
                        Vector2 diff = character.Submarine.SimPosition - Target.Submarine.SimPosition;
                        currTargetSimPos -= diff;
                    }
                }
                character.AIController.SteeringManager.SteeringSeek(currTargetSimPos);
                if (getDivingGearIfNeeded)
                {
                    bool needsDivingGear = HumanAIController.NeedsDivingGear(targetHull);
                    bool needsDivingSuit = needsDivingGear && (targetHull == null || targetIsOutside || targetHull.WaterPercentage > 90);
                    bool needsEquipment = false;
                    if (needsDivingSuit)
                    {
                        needsEquipment = !HumanAIController.HasDivingSuit(character);
                    }
                    else if (needsDivingGear)
                    {
                        needsEquipment = !HumanAIController.HasDivingMask(character);
                    }
                    if (needsEquipment)
                    {
                        TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager));
                    }
                }
            }
        }

        private bool isCompleted;
        public override bool IsCompleted()
        {
            // First check the distance
            // Then the custom condition
            // And finally check if can interact (heaviest)
            if (isCompleted) { return true; }
            if (Target == null)
            {
                abandon = true;
                return false;
            }
            bool closeEnough = Vector2.DistanceSquared(Target.WorldPosition, character.WorldPosition) < CloseEnough * CloseEnough;
            if (repeat)
            {
                if (closeEnough)
                {
                    character.AIController.SteeringManager.Reset();
                    character.AnimController.TargetDir = Target.WorldPosition.X > character.WorldPosition.X ? Direction.Right : Direction.Left;
                }
                return false;
            }
            else if (closeEnough)
            {
                if (customCondition == null || customCondition())
                {
                    if (Target is Item item)
                    {
                        if (character.CanInteractWith(item, out _, checkLinked: false)) { isCompleted = true; }
                    }
                    else if (Target is Character targetCharacter)
                    {
                        if (character.CanInteractWith(targetCharacter, CloseEnough)) { isCompleted = true; }
                    }
                    else
                    {
                        isCompleted = true;
                    }
                }
            }
            if (isCompleted)
            {
                character.AIController.SteeringManager.Reset();
                character.AnimController.TargetDir = Target.WorldPosition.X > character.WorldPosition.X ? Direction.Right : Direction.Left;
            }
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveGoTo objective)) { return false; }
            return objective.Target == Target;
        }

        private void CalculateCloseEnough()
        {
            float interactionDistance = Target is Item i ? i.InteractDistance * 0.9f : 0;
            CloseEnough = Math.Max(interactionDistance, CloseEnough);
        }
    }
}
