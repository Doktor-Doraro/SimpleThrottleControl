﻿using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // =====================================================
        // Settings 
        // Modify these settings to adjust how everything works
        // =====================================================

        //These values effect how quickly the ship will correct its speed over time

        //This setting effects the slope of the thrusters
        //larger values mean more accurate speeds but more 
        //jitter (quick change forward/backward) default 1.5f
        const float MULTIPLIER = 1.5f;

        //This is the addition for using hydrogen thrusters. It can range 
        //from 0 to MULTIPLIER, where 0 is normal operation (eco_mode off) and 
        //MULTIPLIER is no hydrogen thrusters.
        float HYDROGEN_MULTIPLIER = 0.1f;

        //This determins what happens when you leave the cockpit.
        //true: The ship will return to normal mode
        //false: The ship will stay on its last mode
        bool SET_MODE_0_ON_EXIT = true;


        //This is the Dead Zone for speed, if speed is within this range +/-
        //the ship won’t keep trying to adjust the speed
        const float deadZone = 0.0f;

        //This will make the script try to use atmospheric and ion thrusters 
        //over hydrogen thrusters when trying to accelerate/decelerate the ship
        bool eco_mode = true;

        //enable text screens
        bool ENABLE_LCD = true;
        //Small screen mode is good for small LCD's that are hard to read
        //Recommended to be set to true for the fighter cockpit
        bool SMALL_LCD_MODE = false;
        //show stats on the main cockpit LCD
        bool ENABLE_COCKPIT_LCD = true;
        //This is the tag for LCD's that you want to display some stats on
        const string LCD_TAG = "!Throttle";

        //This is the index for the LCD panels in the cockpit
        const int LCD_INDEX = 1;

        //Frequency LCD's are updated
        //Smaller numbers will decrees performance
        //minimum value 0
        const int UPDATE_INTERVAL = 30;

        //The delay between key presses to activate different modes
        //this is in ticks, there are 60 ticks in a second.
        const int DOUBLE_TAP_DELAY = 15;

        //The word that a cockpit or remote control block 
        //will need to be considerd the Main control block.
        //This word can be anywhere in the blocks name.
        const string MAIN_CONTROL_BLOCK_WORD = "!main";

        //Time offset used to adjust accuracy of the
        //lcd delay number.
        const float TIME_OFFSET = 0.35f;

        // ========================================
        // DO NOT EDIT BELOW THIS LINE!!!
        // ========================================

        const string RUNNING_ECHO = "\nType a mode into the argument to change modes\nAvailable Modes (not case sensative)\nNormal\nCruise\nCruise+\nDecoupled\nStop\nEco\nEco on\nEco off\nAny number from 0 to max speed";

        //Lists of blocks for use by the script
        List<IMyCockpit> cockpits = new List<IMyCockpit>();
        List<IMyRemoteControl> remotes = new List<IMyRemoteControl>();
        List<IMyThrust> thrusters = new List<IMyThrust>();
        List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
        List<IMyThrust> forwardThrusters;
        List<IMyThrust> backwardThrusters;
        List<IMyThrust> upThrusters;
        List<IMyThrust> downThrusters;
        List<IMyThrust> leftThrusters;
        List<IMyThrust> rightThrusters;

        //basic variables
        bool setup = true;
        bool enableCruisControl = false;
        bool is_active = true;
        bool lastKeyForward = false;
        bool lastKeyBackward = false;
        int tick = 0;
        int runningNum = 0;
        int lastTickForward = 0;
        int lastTickBackward = 0;
        int lastLCDTick = 0;
        byte flightMode = 0;
        double targetSpeed;
        double currentSpeed;
        float throttle;
        float throttleHY;
        float throttleFB;
        float throttleUD;
        float throttleLR;
        Vector3D targetVectorSpeed = Vector3D.Zero;
        Vector3D currentVectorSpeed = Vector3D.Zero;

        //Get the blocks required to run the script
        IMyCockpit controlSeat = null;
        IMyRemoteControl controlRemote = null;
        IMyTextSurfaceProvider cockpitLCDSurface;
        IMyTextSurface cockpitLCD;


        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            //disable any thrust overrides when a saved game was started
            DisableThrusterOverideAll();
        }

        public void Main(string argument, UpdateType updateSource)
        {   //update tick for the script to keep track of time
            //make sure ticks does not go above its maximum
            if (tick > 2000000000)
                tick = 0;

            tick++;

            //These are values that require to be run first
            if (setup)
            {
                SetupScript();
            }
            //This is the large block of code that runs when everything worked in the setup
            else
            {
                MainScript(argument);
            }

        }

        /* 
         * Setup Loop code
         */
        public void SetupScript()
        {
            int remoteControlBlockIndex = -1;
            int cockpitBlockIndex = -1;
            int checkedCockpit = -1;
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(cockpits, cockPit => cockPit.IsWorking);
            GridTerminalSystem.GetBlocksOfType(remotes, remoteCon => remoteCon.IsWorking);
            GridTerminalSystem.GetBlocksOfType(thrusters);
            GridTerminalSystem.GetBlocksOfType(textPanels, blockName => blockName.DisplayNameText.Contains(LCD_TAG));

            //remove all blocks not connected to the same grid
            cockpits = RemoveSupergridBlocks(cockpits);
            remotes = RemoveSupergridBlocks(remotes);
            thrusters = RemoveSupergridBlocks(thrusters);

            //Get remote blocks
            int mainRemoteCount = 0;
            int mainCockpitCount = 0;
            for (int i = 0; i < remotes.Count; i++)
            {
                if (remotes[i].DisplayNameText.ToLower().Contains(MAIN_CONTROL_BLOCK_WORD.ToLower()))
                {
                    remoteControlBlockIndex = i;
                    mainRemoteCount++;
                }
            }
            for (int i = 0; i < cockpits.Count; i++)
            {
                if (cockpits[i].DisplayNameText.ToLower().Contains(MAIN_CONTROL_BLOCK_WORD.ToLower()))
                {
                    cockpitBlockIndex = i;
                    mainCockpitCount++;
                }
                if (cockpits[i].IsMainCockpit)
                    checkedCockpit = i;
            }

            //Make sure there is a Main Cockpit
            if (mainCockpitCount > 1 || mainRemoteCount > 1)
            {
                Echo("There is more then one Main Cockpit or Remote Control!");
                flightMode = 0;
            }
            else if (mainCockpitCount == 0 && mainRemoteCount == 0 && checkedCockpit == -1)
            {

                Echo("There are no Main Cockpits or Main Remote Control blocks on your ship\n");
                Echo("Help:");
                Echo("Please Add the word  \"" + MAIN_CONTROL_BLOCK_WORD + "\" (without quotes) to the name of a cockpit or remote control block. Then click Recompile Script");
                flightMode = 0;
            }
            else
            {
                if (mainCockpitCount > 0)
                    controlSeat = cockpits[0];
                else if (mainCockpitCount == 0 && checkedCockpit != -1)
                    controlSeat = cockpits[checkedCockpit];
                if (remoteControlBlockIndex != -1)
                    controlRemote = remotes[remoteControlBlockIndex];

                //update the forward thruster list when a player enters the main cockpit
                if (forwardThrusters == null)
                {
                    Echo("Enter the main cockpit to initialise thrusters or control the remote control block");
                    WriteStatsToLCD("Enter the main cockpit to initialise thrusters or control the remote control block");


                    if (IsReadyAndControlled(controlSeat))
                    {
                        GetForwardThrusters(thrusters, ref forwardThrusters);
                        GetBackwardThrusters(thrusters, ref backwardThrusters);
                        GetUpThrusters(thrusters, ref upThrusters);
                        GetDownThrusters(thrusters, ref downThrusters);
                        GetLeftThrusters(thrusters, ref leftThrusters);
                        GetRightThrusters(thrusters, ref rightThrusters);
                        setup = false;
                        flightMode = 1;
                        Echo(RUNNING_ECHO);
                    }
                    else if (IsReadyAndControlled(controlRemote))
                    {
                        GetForwardThrusters(thrusters, ref forwardThrusters);
                        GetBackwardThrusters(thrusters, ref backwardThrusters);
                        GetUpThrusters(thrusters, ref upThrusters);
                        GetDownThrusters(thrusters, ref downThrusters);
                        GetLeftThrusters(thrusters, ref leftThrusters);
                        GetRightThrusters(thrusters, ref rightThrusters);
                        setup = false;
                        flightMode = 1;
                        Echo(RUNNING_ECHO);
                    }

                }
                //Try to find the Cockpit LCD and update it
                try
                {
                    cockpitLCDSurface = controlSeat;
                    cockpitLCD = cockpitLCDSurface.GetSurface(LCD_INDEX);

                    //initialize LCD's
                    foreach (IMyTextPanel lcd in textPanels)
                    {
                        lcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                    }

                    cockpitLCD.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                }
                catch (Exception e)
                {
                    Echo("There is no LCD with the index '" + LCD_INDEX + "' In the main cockpit, please change LCD_INDEX!");
                    ENABLE_COCKPIT_LCD = false;
                }
            }
        }

        /*
         * Main Loop Code
         */
        public void MainScript(string argument)
        {
            CheckControlSeat();

            //Display working symbol and help
            if (tick % 35 == 0 || is_active == false)
            {
                if (is_active)
                    Echo("Script running...");
                else
                {
                    Echo("Script running...");
                    Echo("Cockpit/remote not occupied");
                }
                runningNum++;
                switch (runningNum)
                {
                    case 0:
                        Echo("/");
                        break;
                    case 1:
                        Echo("--");
                        break;
                    case 2:
                        Echo("\\");
                        break;
                    case 3:
                        Echo("|");
                        runningNum = -1;
                        break;
                }
                Echo(RUNNING_ECHO);
            }
            //handle arguments
            string arg = argument.ToLower();
            if (arg.Contains("stop") || arg.Contains("normal"))
            {
                flightMode = 0;
                DisableThrusterOverideAll();
            }
            else if (arg.Contains("eco"))
            {
                if (arg.Contains("on"))
                    eco_mode = true;
                else if (arg.Contains("off"))
                    eco_mode = false;
                else
                    eco_mode = !eco_mode;
            }
            else if (arg.Contains("cruise+"))
                flightMode = 1;
            else if (arg.Contains("cruise"))
                flightMode = 2;
            else if (arg.Contains("decoupled"))
                flightMode = 3;
            else if (arg != "")
                try //Make sure the program dosent crash if the user enters something that is not a double
                {
                    targetSpeed = double.Parse(argument);
                    if (targetSpeed > 0)
                        enableCruisControl = true;
                }
                catch
                {
                    Echo("\"" + arg + "\" is not a valid number!\n" + RUNNING_ECHO);
                }


            //update displays
            double timeToDistance = Math.Round(GetTimeToSpeed(targetSpeed, currentSpeed, forwardThrusters));
            WriteStatsToLCD(currentSpeed, targetSpeed, throttle, timeToDistance, flightMode);
            int shipDirZ = 0;
            int shipDirY = 0;
            int shipDirX = 0;

            //Get the XYZ directions for the remote control or the cockpit
            if (IsReadyAndControlled(controlSeat))
            {
                shipDirZ = GetShipsDesiredDirection(controlSeat).Z;
                shipDirY = GetShipsDesiredDirection(controlSeat).Y;
                shipDirX = GetShipsDesiredDirection(controlSeat).X;
            }
            if (IsReadyAndControlled(controlRemote))
            {
                shipDirZ = GetShipsDesiredDirection(controlRemote).Z;
                shipDirY = GetShipsDesiredDirection(controlRemote).Y;
                shipDirX = GetShipsDesiredDirection(controlRemote).X;
            }

            //if flightMode not 0 run the script
            switch (flightMode)
            {
                //Flight Mode 0: Normal flight, all that happens is the script checks for double taps to allow mode change
                case 0:
                    ModeNormal(shipDirZ);
                    break;

                //Flight Mode 1: Cruise, The script will attempt to keep the ship moving forward at a set speed, stops if revers is pressed.
                case 1:
                    ModeCruise(shipDirZ);
                    break;
                //Flight mode 2: Decoupled mode, the script will maintain a set speed both forward and backwards, pressing backwards only slows down but dosent stop
                case 2:
                    ModeCruisePlus(shipDirZ);
                    break;
                //Flight mode 3: Decoupled+ mode Script keeps the ship flying in any direction.
                case 3:
                    ModeDecoupled(shipDirX, shipDirY, shipDirZ);
                    break;
            }
        }

        /* 
         * Flight Mode 0: Normal flightA
         * This mode just checks for mode changes
         */
        public void ModeNormal(int shipDirZ)
        {
            switch (shipDirZ)
            {

                case -1:    //forward (yes, a negative value means forward)
                    lastKeyForward = true;
                    break;
                case 1:     //backwards (because vectors are weird)
                    lastKeyBackward = true;
                    break;
                case 0:     //Nothing (finally something that makes sense)

                    CheckDoubleTapRelease();
                    break;
            }
        }

        /* 
         * Flight Mode 1: Cruise, The script will attempt to keep the ship moving 
         * forward at a set speed, stops if revers is pressed.
         */
        public void ModeCruise(int shipDirZ)
        {
            //get the ships speed
            currentSpeed = GetShipDirectionalSpeed(Base6Directions.Direction.Forward);

            //determines if the ship is trying to move forwards, backwards or nither
            switch (shipDirZ)
            {
                case -1:    //forward (yes, a negative value means forward)
                    enableCruisControl = true;
                    DisableThrusterOverideAll();
                    targetSpeed = GetShipDirectionalSpeed(Base6Directions.Direction.Forward);
                    lastKeyForward = true;
                    break;
                case 1:     //backwards (because vectors are weird)
                    enableCruisControl = false;
                    targetSpeed = 0;
                    DisableThrusterOverideAll();
                    lastKeyBackward = true;
                    break;
                case 0:     //Nothing (finally something that makes sense)
                    if (enableCruisControl)
                    {
                        //get the speed wanted
                        float speedDifferance = (float)(targetSpeed - currentSpeed);
                        if (eco_mode)
                            throttleHY = GetShipThrottle(HYDROGEN_MULTIPLIER, speedDifferance, 1.0f, 0.0001f);
                        throttle = GetShipThrottle(MULTIPLIER, speedDifferance, 1.0f, 0.0001f);
                        //If vessel is going faster then the wanted speed
                        if (speedDifferance > deadZone)
                        {
                            if (eco_mode)
                                SetSeparateThrusterPercent(throttle, throttleHY, ref forwardThrusters);
                            else
                                SetThrusterPercent(throttle, ref forwardThrusters);
                        }
                        else if (speedDifferance < -deadZone)
                        {
                            if (eco_mode)
                                SetSeparateThrusterPercent(throttle, throttleHY, ref backwardThrusters);
                            else
                                SetThrusterPercent(throttle, ref backwardThrusters);
                        }
                        else
                        {
                            SetThrusterByVal(0.0001f, ref forwardThrusters);
                            SetThrusterByVal(0.0001f, ref backwardThrusters);
                        }
                    }

                    CheckDoubleTapRelease();
                    break;
            }
        }

        /* Flight mode 2: Cruise+ mode, the script will maintain a set speed both forward and backwards.
         * pressing backwards only slows down but dosent stop.
         */
        public void ModeCruisePlus(int shipDirZ)
        {
            //get the ships speed
            currentSpeed = GetShipDirectionalSpeed(Base6Directions.Direction.Forward);

            //determines if the ship is trying to move forwards, backwards or nither
            switch (shipDirZ)
            {
                case -1:    //forward (yes, a negative value means forward)
                    enableCruisControl = true;
                    DisableThrusterOverideAll();
                    targetSpeed = GetShipDirectionalSpeed(Base6Directions.Direction.Forward);
                    lastKeyForward = true;
                    break;
                case 1:     //backwards (because vectors are weird)
                    targetSpeed = GetShipDirectionalSpeed(Base6Directions.Direction.Forward);
                    DisableThrusterOverideAll();
                    lastKeyBackward = true;
                    break;
                case 0:     //Nothing (finally something that makes sense)
                    if (enableCruisControl)
                    {
                        //get the speed wanted
                        float speedDifferance = (float)(targetSpeed - currentSpeed);
                        if (eco_mode)
                            throttleHY = GetShipThrottle(HYDROGEN_MULTIPLIER, speedDifferance, 1.0f, 0.0001f);
                        throttle = GetShipThrottle(MULTIPLIER, speedDifferance, 1.0f, 0.0001f);
                        //If vessel is going faster then the wanted speed
                        if (speedDifferance > deadZone)
                        {
                            if (eco_mode)
                                SetSeparateThrusterPercent(throttle, throttleHY, ref forwardThrusters);
                            else
                                SetThrusterPercent(throttle, ref forwardThrusters);
                        }
                        else if (speedDifferance < -deadZone)
                        {
                            if (eco_mode)
                                SetSeparateThrusterPercent(throttle, throttleHY, ref backwardThrusters);
                            else
                                SetThrusterPercent(throttle, ref backwardThrusters);
                        }
                        else
                        {
                            SetThrusterByVal(0.0001f, ref forwardThrusters);
                            SetThrusterByVal(0.0001f, ref backwardThrusters);
                        }
                    }

                    CheckDoubleTapRelease();
                    break;
            }
        }

        /*
         * Flight mode 3: Decoupled mode Script keeps the ship flying in any direction.
         */
        public void ModeDecoupled(int shipDirX, int shipDirY, int shipDirZ)
        {
            //Get current speed
            currentSpeed = controlSeat.GetShipSpeed();
            //current speed X
            currentVectorSpeed.X = GetShipDirectionalSpeed(Base6Directions.Direction.Up);
            //current Speed Y
            currentVectorSpeed.Y = GetShipDirectionalSpeed(Base6Directions.Direction.Right);
            //Current speed Z
            currentVectorSpeed.Z = GetShipDirectionalSpeed(Base6Directions.Direction.Forward);


            if (shipDirZ == -1)      //forward (yes, a negative value means forward)
            {
                enableCruisControl = true;
                targetVectorSpeed.Z = currentVectorSpeed.Z;
                DisableThrusterOveride(forwardThrusters);
                DisableThrusterOveride(backwardThrusters);
                lastKeyBackward = true;
            }
            else if (shipDirZ == 1)  //backwards (because vectors are weird)
            {
                enableCruisControl = true;
                targetVectorSpeed.Z = currentVectorSpeed.Z;
                DisableThrusterOveride(forwardThrusters);
                DisableThrusterOveride(backwardThrusters);
                lastKeyBackward = true;
            }

            if (shipDirX == -1)      //right
            {
                enableCruisControl = true;
                DisableThrusterOveride(leftThrusters);
                DisableThrusterOveride(rightThrusters);
                targetVectorSpeed.Y = currentVectorSpeed.Y;
            }
            else if (shipDirX == 1)  //left
            {
                enableCruisControl = true;
                DisableThrusterOveride(leftThrusters);
                DisableThrusterOveride(rightThrusters);
                targetVectorSpeed.Y = currentVectorSpeed.Y;
            }

            if (shipDirY == -1)      //down
            {
                enableCruisControl = true;
                DisableThrusterOveride(upThrusters);
                DisableThrusterOveride(downThrusters);
                targetVectorSpeed.X = currentVectorSpeed.X;
            }
            else if (shipDirY == 1)  //Up
            {
                enableCruisControl = true;
                DisableThrusterOveride(upThrusters);
                DisableThrusterOveride(downThrusters);
                targetVectorSpeed.X = currentVectorSpeed.X;
            }



            if (shipDirZ == 0 && shipDirY == 0 && shipDirX == 0)     //No buttons held
            {
                if (enableCruisControl)
                {
                    //get current speed
                    float speedDifferanceFB = (float)(targetVectorSpeed.Z - currentVectorSpeed.Z);
                    float speedDifferanceUD = (float)(targetVectorSpeed.X - currentVectorSpeed.X);
                    float speedDifferanceLR = (float)(targetVectorSpeed.Y - currentVectorSpeed.Y);
                    throttle = 0;

                    //Forward/back
                    if (speedDifferanceFB > deadZone)
                    {
                        throttleFB = GetShipThrottle(MULTIPLIER, speedDifferanceFB, 1.0f, 0.0001f);
                        throttle += throttleFB;
                        SetThrusterPercent(throttleFB, ref forwardThrusters);
                    }
                    else if (speedDifferanceFB < -deadZone)
                    {
                        throttleFB = GetShipThrottle(MULTIPLIER, speedDifferanceFB, 1.0f, 0.0001f);
                        throttle += throttleFB;
                        SetThrusterPercent(throttleFB, ref backwardThrusters);
                    }
                    else
                    {
                        SetThrusterByVal(0.0001f, ref forwardThrusters);
                        SetThrusterByVal(0.0001f, ref backwardThrusters);
                    }
                    //Left/right
                    if (speedDifferanceLR > deadZone)
                    {
                        throttleLR = GetShipThrottle(MULTIPLIER, speedDifferanceLR, 1.0f, 0.0001f);
                        throttle += throttleLR;
                        SetThrusterPercent(throttleLR, ref leftThrusters);
                    }
                    else if (speedDifferanceLR < -deadZone)
                    {
                        throttleLR = GetShipThrottle(MULTIPLIER, speedDifferanceLR, 1.0f, 0.0001f);
                        throttle += throttleLR;
                        SetThrusterPercent(throttleLR, ref rightThrusters);
                    }
                    else
                    {
                        SetThrusterByVal(0.0001f, ref leftThrusters);
                        SetThrusterByVal(0.0001f, ref rightThrusters);
                    }
                    //Up/Down
                    if (speedDifferanceUD > deadZone)
                    {
                        throttleUD = GetShipThrottle(MULTIPLIER, speedDifferanceUD, 1.0f, 0.0001f);
                        throttle += throttleUD;
                        SetThrusterPercent(throttleUD, ref upThrusters);
                    }
                    else if (speedDifferanceUD < -deadZone)
                    {
                        throttleUD = GetShipThrottle(MULTIPLIER, speedDifferanceUD, 1.0f, 0.0001f);
                        throttle += throttleUD;
                        SetThrusterPercent(throttleUD, ref downThrusters);
                    }
                    else
                    {
                        SetThrusterByVal(0.0001f, ref upThrusters);
                        SetThrusterByVal(0.0001f, ref downThrusters);
                    }

                    /*WriteStatsToLCD( 
                        "Forward: " + currentVectorSpeed.Z + 
                        "\nForward: " + targetVectorSpeed.Z +
                        "\nDif: " + speedDifferanceFB +
                        "\nRight: " + currentVectorSpeed.Y + 
                        "\nRight: " + targetVectorSpeed.Y +
                        "\nDif: " + speedDifferanceLR +
                        "\nUp: " + currentVectorSpeed.X + 
                        "\nUp: " + targetVectorSpeed.X +
                        "\nDif: " + speedDifferanceUD);*/
                }
                CheckDoubleTapRelease();
            }
        }

        public void CheckDoubleTapRelease()
        {
            //Double tap check on key releas
            if (lastKeyForward)
            {
                bool check = CheckTapForward();
                if (check)
                    flightMode = getFlightMode(flightMode, true);
                lastKeyForward = false;
            }

            if (lastKeyBackward)
            {
                bool check = CheckTapBackward();
                if (check)
                {
                    flightMode = getFlightMode(flightMode, false);
                }
                lastKeyBackward = false;
            }
        }

        /*
         * Check to make sure the cockpit/remote block still works and is controlled
         */
        public void CheckControlSeat()
        {
            
            if (controlSeat != null && controlRemote == null)
            {
                if (controlSeat.IsUnderControl == false)
                {
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    EmergencyStop(SET_MODE_0_ON_EXIT);
                    is_active = false;
                }
                else if (!is_active)
                {
                    is_active = true;
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                }
            }
            else if (controlSeat != null && controlRemote != null)
            {
                if (controlSeat.IsUnderControl == false && controlRemote.IsUnderControl == false)
                {
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    EmergencyStop(SET_MODE_0_ON_EXIT);
                    is_active = false;
                }
                else if (!is_active)
                {
                    is_active = true;
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                }
            }
            else if (controlSeat == null && controlRemote != null)
            {
                if (controlRemote.IsUnderControl == false)
                {
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    EmergencyStop(SET_MODE_0_ON_EXIT);
                    is_active = false;
                }
                else if (!is_active)
                {
                    is_active = true;
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                }
            }
            else if (controlSeat == null && controlRemote == null)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                EmergencyStop(true);
                setup = true;
                is_active = false;
            }
        }
        /*
        * Returns the thrusters facing forward for a given list of thrusters
        */
        public void GetForwardThrusters(List<IMyThrust> allThrusters, ref List<IMyThrust> foundForwardThrusters)
        {
            foundForwardThrusters = new List<IMyThrust>();
            foreach (IMyThrust currThruster in allThrusters)
                if (currThruster.GridThrustDirection.Z == 1)
                    foundForwardThrusters.Add(currThruster);
        }

        /*
        * Updates the thrusters facing Backward for a given list of thrusters
        */
        public void GetBackwardThrusters(List<IMyThrust> allThrusters, ref List<IMyThrust> foundForwardThrusters)
        {
            foundForwardThrusters = new List<IMyThrust>();
            foreach (IMyThrust currThruster in allThrusters)
                if (currThruster.GridThrustDirection.Z == -1)
                    foundForwardThrusters.Add(currThruster);
        }

        /*
        * Updates the thrusters facing Up for a given list of thrusters
        */
        public void GetUpThrusters(List<IMyThrust> allThrusters, ref List<IMyThrust> foundForwardThrusters)
        {
            foundForwardThrusters = new List<IMyThrust>();
            foreach (IMyThrust currThruster in allThrusters)
                if (currThruster.GridThrustDirection.Y == -1)
                    foundForwardThrusters.Add(currThruster);
        }

        /*
        * Updates the thrusters facing Down for a given list of thrusters
        */
        public void GetDownThrusters(List<IMyThrust> allThrusters, ref List<IMyThrust> foundForwardThrusters)
        {
            foundForwardThrusters = new List<IMyThrust>();
            foreach (IMyThrust currThruster in allThrusters)
                if (currThruster.GridThrustDirection.Y == 1)
                    foundForwardThrusters.Add(currThruster);
        }

        /*
        * Updates the thrusters facing Left for a given list of thrusters
        */
        public void GetLeftThrusters(List<IMyThrust> allThrusters, ref List<IMyThrust> foundForwardThrusters)
        {
            foundForwardThrusters = new List<IMyThrust>();
            foreach (IMyThrust currThruster in allThrusters)
                if (currThruster.GridThrustDirection.X == -1)
                    foundForwardThrusters.Add(currThruster);
        }

        /*
        * Updates the thrusters facing Right for a given list of thrusters
        */
        public void GetRightThrusters(List<IMyThrust> allThrusters, ref List<IMyThrust> foundForwardThrusters)
        {
            foundForwardThrusters = new List<IMyThrust>();
            foreach (IMyThrust currThruster in allThrusters)
                if (currThruster.GridThrustDirection.X == 1)
                    foundForwardThrusters.Add(currThruster);
        }

        /*
        * Updates the direction a ship is trying to go
        */
        public Vector3I GetShipsDesiredDirection(IMyCockpit oriantationBlock)
        {
            Vector3I direction;

            direction.X = (int)Math.Ceiling(oriantationBlock.MoveIndicator.X);
            direction.Y = (int)Math.Ceiling(oriantationBlock.MoveIndicator.Y);
            direction.Z = (int)Math.Ceiling(oriantationBlock.MoveIndicator.Z);

            return direction;
        }

        public Vector3I GetShipsDesiredDirection(IMyRemoteControl oriantationBlock)
        {
            Vector3I direction;

            direction.X = (int)Math.Ceiling(oriantationBlock.MoveIndicator.X);
            direction.Y = (int)Math.Ceiling(oriantationBlock.MoveIndicator.Y);
            direction.Z = (int)Math.Ceiling(oriantationBlock.MoveIndicator.Z);

            return direction;
        }

        public Vector3I GetShipsDesiredDirection()
        {
            if (IsReadyAndControlled(controlSeat))
            {
                return GetShipsDesiredDirection(controlSeat);
            }
            else if (IsReadyAndControlled(controlRemote))
            {
                return GetShipsDesiredDirection(controlRemote);
            }
            else
                return Vector3I.Zero;
        }

        /*
        * Sets all the thrusters in the list to a percent where 0 is off and 1 is full
        */
        public void SetThrusterPercent(float percent, ref List<IMyThrust> thrusters)
        {
            
            
            if (thrusters != null)
            {
                for (int i = 0; i < thrusters.Count; i++)
                {
                    thrusters[i].ThrustOverridePercentage = percent;
                }
            }
        }

        /*
        * Sets all the thrusters in the list to a percent where 0 is off and 1 is full
        */
        public void SetSeparateThrusterPercent(float electroPercent, float hydrogenPercent, ref List<IMyThrust> thrusters)
        {
            
            
            if (thrusters != null)
            {
                for (int i = 0; i < thrusters.Count; i++)
                {
                    if (thrusters[i].BlockDefinition.SubtypeName.Contains("Hydrogen"))
                    {
                        thrusters[i].ThrustOverridePercentage = hydrogenPercent;
                    }
                    
                    else
                        thrusters[i].ThrustOverridePercentage = electroPercent;

                }
            }
        }

        /*
        * Sets all the thrusters in the list to an amount where 0 is off and 1 is full
        */
        public void SetThrusterByVal(float val, ref List<IMyThrust> thrusters)
        {
            
            
            if (thrusters != null)
            {
                for (int i = 0; i < thrusters.Count; i++)
                {
                    thrusters[i].ThrustOverride = Math.Abs(val);
                }
            }
        }

        public void DisableThrusterOveride(List<IMyThrust> thrusters)
        {
            
            
            if (thrusters != null)
                foreach (IMyThrust thruster in thrusters)
                {
                    thruster.ThrustOverride = -1;
                }
        }

        public void DisableThrusterOverideAll()
        {
            
            
            if (thrusters != null)
                foreach (IMyThrust thruster in thrusters)
                {
                    thruster.ThrustOverride = -1;
                }
        }

        public string GetTextForLCD(double speed, double targetSpeed, float throttleOutput, double timeToSpeed, int running)
        {

            string cursor;
            string output;
            string state;
            string eco = eco_mode ? "ON" : "OFF";
            switch (runningNum)
            {
                case 0:
                    cursor = "/";
                    break;
                case 1:
                    cursor = "--";
                    break;
                case 2:
                    cursor = "\\";
                    break;
                default:
                    cursor = "|";
                    break;
            }
            output = "Status: " + cursor;
            output += "\nSpeed: " + Math.Round(speed, 2) + "m / s" + '\n' +
                        "Target:          " + Math.Round(targetSpeed, 2) + "m/s" + '\n' +
                        "Delay:           " + timeToSpeed + "s" + '\n' +
                        "Throttle:        " + Math.Round(throttleOutput * 100, 1) + "%" + '\n' +
                        "Eco Mode:     " + eco + '\n';

            if (SMALL_LCD_MODE)
            {
				switch (flightMode)

				{
                    case 0:
                        state = "NORMAL";
                        break;
                    case 1:
                        state = "CRUISE";
                        break;
                    case 2:
                        state = "CRUISE+";
                        break;
                    case 3:
                        state = "DECOUPLED";
                        break;
                    default:
                        state = "UNKNOWN: " + flightMode;
                        break;
                }
            }
            else
            {
                output += "\\/  Flight Mode  \\/\n";

                switch (flightMode)
                {
                    case 0:
                        state = (
                                    "DECOUPLED" + '\n' +
                                    "CRUISE" + '\n' +
                                    "NORMAL           <==" + '\n'
                                );
                        break;
                    case 1:
                        state = (
                                    "DECOUPLED" + '\n' +
                                    "CRUISE             <==" + '\n' +
                                    "NORMAL" + '\n'
                                );
                        break;
                    case 2:
                        state = (
                                    "DECOUPLED" + '\n' +
                                    "CRUISE+           <==" + '\n' +
                                    "NORMAL" + '\n'
                                );
                        break;
                    case 3:
                        state = (
                                    "DECOUPLED  <==" + '\n' +
                                    "CRUISE" + '\n' +
                                    "NORMAL" + '\n'
                                );
                        break;
                    default:
                        state = "Unknown Mode: " + flightMode;
                        break;
                }
            }
            output += state;
            return output;
        }


        private void WriteStatsToLCD(double speed, double targetSpeed, float throttleOutput, double timeToSpeed, int running)
        {
            if (tick - lastLCDTick > UPDATE_INTERVAL)
            {
                lastLCDTick = tick;

                //Text Panels
                if (ENABLE_LCD)
                {
                    foreach (IMyTextPanel lcd in textPanels)
                    {
                        lcd.WriteText(GetTextForLCD(speed, targetSpeed, throttleOutput, timeToSpeed, running));
                    }
                }

                //LCD panels (cockpit LCD)
                if (ENABLE_COCKPIT_LCD)
                {
                    cockpitLCD.WriteText(GetTextForLCD(speed, targetSpeed, throttleOutput, timeToSpeed, running));
                }
            }
        }

        //write simple text to text panels only, not Cockpit LCD
        private void WriteStatsToLCD(string message)
        {
            if (tick - lastLCDTick > UPDATE_INTERVAL)
            {
                lastLCDTick = tick;
                if (ENABLE_LCD)
                {
                    foreach (IMyTextPanel lcd in textPanels)
                        lcd.WriteText(message);
                }
            }
        }

        /*
		* Returns a flight mode based on the current flight mode and the direction, true means move up, false down
		*/
        public byte getFlightMode(byte currentMode, bool goUp)
        {

            switch (currentMode)
            {
                case 0:
                    if (goUp)
                        currentMode++;
                    break;
                case 1:
                    if (goUp)
                        currentMode++;
                    else
                    {
                        currentMode--;
                        EmergencyStop(false);
                    }
                    break;
                case 2:
                    if (goUp)
                        currentMode++;
                    else
                    {
                        currentMode--;
                        EmergencyStop(false);
                    }
                    break;
                case 3:
                    if (!goUp)
                    {
                        EmergencyStop(false);
                        currentMode--;
                        targetVectorSpeed.Z = currentVectorSpeed.Z;
                        targetVectorSpeed.Y = currentVectorSpeed.Y;
                        targetVectorSpeed.X = currentVectorSpeed.X;
                    }
                    break;
            }

            return currentMode;
        }

        /*
        * Returns the time it will take for a ship to reach a set speed
        * Uses a list of thrusters, cockpit, a current speed and a wanted speed
        */
        public double GetTimeToSpeed(double wantedSpeed, double currentSpeed, List<IMyThrust> thrustersInDirection)
        {
            float maxthrust = 0;
            float totalMass = 0;
            float speedToReach = (float)(wantedSpeed - currentSpeed);
            float acceleration;
            if (IsReadyAndControlled(controlSeat))
                totalMass = controlSeat.CalculateShipMass().TotalMass;
            if (IsReadyAndControlled(controlRemote))
                totalMass = controlRemote.CalculateShipMass().TotalMass;

            //get the total thrust of the thrusters
            foreach (IMyThrust thruster in thrustersInDirection)
                maxthrust += thruster.MaxEffectiveThrust;

            //get how quickly the ship can accelerate
            acceleration = (maxthrust / totalMass) * TIME_OFFSET;

            return Math.Abs(speedToReach / acceleration);
        }

        /*
        * Returns the speed given a direction
        * Example direction: Base6Directions.Direction.Forward
        */
        public double GetShipDirectionalSpeed(IMyCockpit cockpit, Base6Directions.Direction direction)
        {
            //get the velocity of the ship as a vector
            Vector3D velocity = cockpit.GetShipVelocities().LinearVelocity;

            //given a direction calculate the "length" for that direction, length is the speed in this case
            return velocity.Dot(cockpit.WorldMatrix.GetDirectionVector(direction));
        }

        public double GetShipDirectionalSpeed(IMyRemoteControl remoteBlock, Base6Directions.Direction direction)
        {
            //get the velocity of the ship as a vector
            Vector3D velocity = remoteBlock.GetShipVelocities().LinearVelocity;

            //given a direction calculate the "length" for that direction, length is the speed in this case
            return velocity.Dot(remoteBlock.WorldMatrix.GetDirectionVector(direction));
        }
        public double GetShipDirectionalSpeed(Base6Directions.Direction direction)
        {
            if (IsReadyAndControlled(controlSeat))
            {
                return GetShipDirectionalSpeed(controlSeat, direction);
            }
            else if (IsReadyAndControlled(controlRemote))
            {
                return GetShipDirectionalSpeed(controlRemote, direction);
            }
            else
            {
                return 0.0;
            }
        }

        /*
         * Returns the throttle between two values
         * Throttle is calculated using a parabola
         */
        public float GetShipThrottle(float m, float differance, float max, float min)
        {
            float value = (float)(m * Math.Pow(differance, 2));
            if (value >= max)
                return max;
            else if (value <= min)
                return min;
            else
                return value;
        }

        public bool CheckTapForward()
        {
            bool output = false;
            int diff = 0;
            //Double Click detection
            diff = Math.Abs(tick - lastTickForward);
            if (diff <= DOUBLE_TAP_DELAY)
                output = true;
            else
                lastTickForward = tick;
            return output;
        }

        public bool CheckTapBackward()
        {
            bool output = false;
            int diff = 0;
            //Double Click detection

            diff = Math.Abs(tick - lastTickBackward);
            if (diff <= DOUBLE_TAP_DELAY)
                output = true;
            lastTickBackward = tick;
            return output;
        }

        //Check if a controller is ready and under control
        bool IsReadyAndControlled(IMyCockpit controller)
        {
            if (controller != null)
                if (controller.IsUnderControl)
                    return true;
            //if the tests don't pass
            return false;
        }

        bool IsReadyAndControlled(IMyRemoteControl controller)
        {
            if (controller != null)
                if (controller.IsUnderControl && !controller.IsAutoPilotEnabled)
                    return true;
            //if the tests don't pass
            return false;
        }

        public void EmergencyStop(bool changeMode)
        {
            if (changeMode)
                flightMode = 0;
            enableCruisControl = false;
            DisableThrusterOverideAll();

        }

        //Go through and remove all blocks not on the same grid
        public List<IMyCockpit> RemoveSupergridBlocks(List<IMyCockpit> myCockpits)
        {
            List<IMyCockpit> newList =  new List<IMyCockpit>();
            foreach(IMyCockpit cur in myCockpits)
            {
                if (cur.CubeGrid == Me.CubeGrid)
                {
                    newList.Add(cur);
                }
            }

            return newList;
        }

        public List<IMyRemoteControl> RemoveSupergridBlocks(List<IMyRemoteControl> myCockpits)
        {
            List<IMyRemoteControl> newList = new List<IMyRemoteControl>();
            foreach (IMyRemoteControl cur in myCockpits)
            {
                if (cur.CubeGrid == Me.CubeGrid)
                {
                    newList.Add(cur);
                }
            }

            return newList;
        }

        public List<IMyThrust> RemoveSupergridBlocks(List<IMyThrust> myCockpits)
        {
            List<IMyThrust> newList = new List<IMyThrust>();
            foreach (IMyThrust cur in myCockpits)
            {
                if (cur.CubeGrid == Me.CubeGrid)
                {
                    newList.Add(cur);
                }
            }

            return newList;
        }
    }
}

//that’s all folks
