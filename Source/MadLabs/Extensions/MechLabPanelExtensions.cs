﻿using Harmony;
using BattleTech.UI;
using System.IO;
using System;
using Newtonsoft.Json.Linq;
using BattleTech;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HBS;
using HBS.Collections;
using BattleTech.UI.TMProWrapper;

namespace MadLabs.Extensions
{
    public static class MechLabPanelExtensions
    {
        public static void SetToStock(this MechLabPanel mechLabPanel)
        {
            string stockMechDefId = mechLabPanel.activeMechDef.ChassisID.Replace("chassisdef", "mechdef");

            // Depends on LittleThings.Settings.EnableStockMechReferenceViaMechDefDescriptionModel
            if (!string.IsNullOrEmpty(mechLabPanel.activeMechDef.Description.Model))
            {
                Logger.Debug("[MechLabPanelExtensions.SetToStock] FOUND StockMechReferenceViaMechDefDescriptionModel! Overriding stockMechDefId...");
                Logger.Debug("[MechLabPanelExtensions.SetToStock] mechLabPanel.activeMechDef.Description.Model: " + mechLabPanel.activeMechDef.Description.Model);

                stockMechDefId = mechLabPanel.activeMechDef.Description.Model.Replace("model", "mechdef");
                Logger.Debug("[MechLabPanelExtensions.SetToStock] stockMechDefId: " + stockMechDefId);
            }

            mechLabPanel.ApplyLoadout(stockMechDefId);
        }



        public static void ApplyLoadout(this MechLabPanel mechLabPanel, string mechDefId)
        {
            try
            {
                // Check first
                if (!mechLabPanel.Sim.DataManager.Exists(BattleTechResourceType.MechDef, mechDefId))
                {
                    GenericPopupBuilder
                        .Create("Apply Loadout failed", "The requested MechDef " + mechDefId + " was not found")
                        .AddButton("Confirm", null, true, null)
                        .AddFader(new UIColorRef?(LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.PopupBackfill), 0f, true)
                        .SetAlwaysOnTop()
                        .SetOnClose(delegate
                        {
                            // Nothing
                        })
                        .Render();

                    // Abort!
                    return;
                }
                MechDef activeMechDef = mechLabPanel.activeMechDef;
                MechDef requestedMechDef = new MechDef(mechLabPanel.Sim.DataManager.MechDefs.Get(mechDefId), null, true);

                // Check second
                List<string> errorDescriptions = new List<string>();
                if (!mechLabPanel.CanApplyLoadout(requestedMechDef, out errorDescriptions))
                {
                    string popupTitle = "Apply Loadout failed";
                    //string popupBody = "The following problems were encountered:" + Environment.NewLine;
                    string popupBody = "";

                    foreach (string errorDescription in errorDescriptions)
                    {
                        popupBody += errorDescription + Environment.NewLine;
                    }

                    GenericPopupBuilder
                        .Create(popupTitle, popupBody)
                        .AddButton("Confirm", null, true, null)
                        .AddFader(new UIColorRef?(LazySingletonBehavior<UIManager>.Instance.UILookAndColorConstants.PopupBackfill), 0f, true)
                        .SetAlwaysOnTop()
                        .SetOnClose(delegate
                        {
                            // Nothing
                        })
                        .Render();

                    // Abort!
                    return;
                }
                // Checks done



                // Hard cleanup upfront
                mechLabPanel.OnRevertMech();

                // Get data
                MechLabInventoryWidget inventoryWidget = (MechLabInventoryWidget)AccessTools.Field(typeof(MechLabPanel), "inventoryWidget").GetValue(mechLabPanel);
                MechLabDismountWidget dismountWidget = (MechLabDismountWidget)AccessTools.Field(typeof(MechLabPanel), "dismountWidget").GetValue(mechLabPanel);
                MechLabMechInfoWidget mechInfoWidget = (MechLabMechInfoWidget)AccessTools.Field(typeof(MechLabPanel), "mechInfoWidget").GetValue(mechLabPanel);
                HBS_InputField mechNickname = (HBS_InputField)AccessTools.Field(typeof(MechLabMechInfoWidget), "mechNickname").GetValue(mechInfoWidget);

                List<MechComponentRef> dropshipInventory = mechLabPanel.Sim.GetAllInventoryItemDefs();
                List<MechComponentRef> storageInventory = mechLabPanel.storageInventory;
                List<MechComponentRef> activeMechInventory = mechLabPanel.activeMechInventory;
                MechComponentRef[] requestedMechComponentsArray = (MechComponentRef[])AccessTools.Field(typeof(MechDef), "inventory").GetValue(requestedMechDef);
                List<MechComponentRef> requestedMechComponents = requestedMechComponentsArray.ToList();

                // Remove fixed equipment as it will be ignored from dismounting et all
                for (int i = requestedMechComponents.Count - 1; i >= 0; i--)
                {
                    if (requestedMechComponents[i].IsFixed)
                    {
                        Logger.Debug("[Extensions.ResetToStock] FOUND AND WILL REMOVE FIXED EQUIPMENT: " + requestedMechComponents[i].ComponentDefID);
                        requestedMechComponents.RemoveAt(i);
                    }
                }

                // This puts the current equipment into dismountWidget and also clears the Mechs inventory
                // NOTE that fixed equipment will stay where it is -> must be removed from requestedComponents manually!
                mechLabPanel.OnStripEquipment();

                // Collect items from dismountWidget and/or inventoryWidget
                List<MechComponentRef> requestedMechComponentsRequired = requestedMechComponents.ToList();

                //List<MechLabItemSlotElement> activeMechDismountedItems = new List<MechLabItemSlotElement>(dismountWidget.localInventory);
                List<MechLabItemSlotElement> activeMechDismountedItems = dismountWidget.localInventory;

                //List<InventoryItemElement_NotListView> localInventoryItems = new List<InventoryItemElement_NotListView>(inventoryWidget.localInventory);
                List<InventoryItemElement_NotListView> localInventoryItems = inventoryWidget.localInventory;

                List<MechLabItemSlotElement> itemsCollectedFromDismount = new List<MechLabItemSlotElement>();
                List<InventoryItemElement_NotListView> itemsCollectedFromInventory = new List<InventoryItemElement_NotListView>();

                // CHECK
                foreach (MechComponentRef comp in requestedMechComponentsRequired)
                {
                    Logger.Debug("[Extensions.ResetToStock] INIT requestedMechComponentsRequired: " + comp.ComponentDefID);
                }



                // Check for required items in dismountWidget first, remove/add from/to applicable Lists
                // @ToDo: Put in method
                for (int i = requestedMechComponentsRequired.Count - 1; i >= 0; i--)
                {
                    bool found = false;
                    for (int j = activeMechDismountedItems.Count - 1; j >= 0; j--)
                    {
                        if (requestedMechComponentsRequired[i].ComponentDefID == activeMechDismountedItems[j].ComponentRef.ComponentDefID)
                        {
                            Logger.Debug("[Extensions.ResetToStock] FOUND in activeMechDismountedItems: " + requestedMechComponentsRequired[i].ComponentDefID);
                            found = true;
                            requestedMechComponentsRequired.RemoveAt(i);
                            itemsCollectedFromDismount.Add(activeMechDismountedItems[j]);

                            // Remove visually
                            // Do not forget to refresh the widget
                            MechLabItemSlotElement mechLabItemSlotElement = activeMechDismountedItems[j];
                            mechLabItemSlotElement.gameObject.transform.SetParent(null, false);
                            mechLabPanel.dataManager.PoolGameObject(MechLabPanel.MECHCOMPONENT_ITEM_PREFAB, mechLabItemSlotElement.gameObject);

                            // Remove data AFTERWARDS too
                            activeMechDismountedItems.RemoveAt(j);

                            break;
                        }
                    }
                    if (!found)
                    {
                        Logger.Debug("[Extensions.ResetToStock] NOT FOUND in activeMechDismountedItems: " + requestedMechComponentsRequired[i].ComponentDefID);
                    }
                }
                // Refresh UI
                ReflectionHelper.InvokePrivateMethode(dismountWidget, "RefreshComponentCountText", null);



                // CHECK
                foreach (MechLabItemSlotElement item in itemsCollectedFromDismount)
                {
                    Logger.Debug("[Extensions.ResetToStock] itemsCollectedFromDismount: " + item.ComponentRef.ComponentDefID + ", MountedLocation: " + item.MountedLocation + ", DropParent: " + item.DropParent);
                }



                // Check for REMAINING required items in inventoryWidget, remove/add from/to applicable Lists
                // NEEDS conversion of remaining components to inventory items via custom type
                List<InventoryItemElement_Simple> requestedMechItemsRequired = Utilities.ComponentsToInventoryItems(requestedMechComponentsRequired, true);
                List<InventoryItemElement_Simple> missingItems = new List<InventoryItemElement_Simple>();
                bool itemsAvailableInInventory = mechLabPanel.ItemsAvailableInInventory(requestedMechItemsRequired, localInventoryItems, out missingItems);
                Logger.Debug("[Extensions.ResetToStock] itemsAvailableInInventory: " + itemsAvailableInInventory);

                if (itemsAvailableInInventory)
                {
                    itemsCollectedFromInventory = mechLabPanel.PullItemsFromInventory(requestedMechItemsRequired, localInventoryItems);
                    // Clear required components list
                    requestedMechComponentsRequired.Clear();
                }
                else
                {
                    // Hard exit, SHOULD NEVER END UP HERE!
                    Logger.Debug("[Extensions.ResetToStock] MISSING ITEMS. ABORTING. YOU SHOULD NEVER SEE THIS!");
                    mechLabPanel.OnRevertMech();
                    return;
                }

                // CHECK
                foreach (InventoryItemElement_NotListView item in itemsCollectedFromInventory)
                {
                    Logger.Debug("[Extensions.ResetToStock] itemsCollectedFromInventory: " + item.ComponentRef.ComponentDefID + ", MountedLocation: " + item.MountedLocation + ", DropParent: " + item.DropParent);
                }



                // At this point inventoryWidget.localInventory AND dismountWidget.localInventory already have the potentially reusable components REMOVED
                // So, in "SetEquipment" they must be SPAWNED otherwise they are lost forever



                // Helper Dictionary
                Dictionary<ChassisLocations, MechLabLocationWidget> LocationHandler = new Dictionary<ChassisLocations, MechLabLocationWidget>();
                LocationHandler.Add(ChassisLocations.Head, mechLabPanel.headWidget);
                LocationHandler.Add(ChassisLocations.CenterTorso, mechLabPanel.centerTorsoWidget);
                LocationHandler.Add(ChassisLocations.LeftTorso, mechLabPanel.leftTorsoWidget);
                LocationHandler.Add(ChassisLocations.RightTorso, mechLabPanel.rightTorsoWidget);
                LocationHandler.Add(ChassisLocations.LeftArm, mechLabPanel.leftArmWidget);
                LocationHandler.Add(ChassisLocations.RightArm, mechLabPanel.rightArmWidget);
                LocationHandler.Add(ChassisLocations.LeftLeg, mechLabPanel.leftLegWidget);
                LocationHandler.Add(ChassisLocations.RightLeg, mechLabPanel.rightLegWidget);

                // Prepare custom equipment with info about desired origin beforehand
                List<InventoryItemElement_Simple> requestedEquipment = new List<InventoryItemElement_Simple>();
                List<MechLabItemSlotElement> dismountedItems = itemsCollectedFromDismount.ToList();
                foreach (MechComponentRef requestedItem in requestedMechComponents)
                {
                    InventoryItemElement_Simple requestedInventoryItem = new InventoryItemElement_Simple();
                    requestedInventoryItem.ComponentRef = requestedItem;
                    requestedInventoryItem.Origin = MechLabDropTargetType.InventoryList;

                    for (int i = dismountedItems.Count - 1; i >= 0; i--)
                    {
                        if (requestedItem.ComponentDefID == dismountedItems[i].ComponentRef.ComponentDefID)
                        {
                            requestedInventoryItem.Origin = MechLabDropTargetType.Dismount;
                            dismountedItems.RemoveAt(i);
                            break;
                        }
                    }
                    requestedEquipment.Add(requestedInventoryItem);
                }
                // CHECK
                foreach (MechComponentRef item in requestedMechComponents)
                {
                    Logger.Debug("[Extensions.ResetToStock] baseMechComponents: " + item.ComponentDefID);
                }
                foreach (InventoryItemElement_Simple item in requestedEquipment)
                {
                    Logger.Debug("[Extensions.ResetToStock] requestedEquipment: " + item.ComponentRef.ComponentDefID + ", Origin: " + item.Origin);
                }

                // Set inventory including a hint to where the components were taken from
                // Example manual call: mechLabPanel.SetEquipment(requestedEquipment, mechLabPanel.centerTorsoWidget, requestedMechDef.GetLocationLoadoutDef(ChassisLocations.CenterTorso));
                foreach (KeyValuePair<ChassisLocations, MechLabLocationWidget> LocationPair in LocationHandler)
                {
                    mechLabPanel.SetEquipment(requestedEquipment, LocationPair.Value, requestedMechDef.GetLocationLoadoutDef(LocationPair.Key));
                    mechLabPanel.SetArmor(LocationPair.Value, requestedMechDef.GetLocationLoadoutDef(LocationPair.Key));
                }

                // Refresh main inventory
                mechLabPanel.activeMechInventory = new List<MechComponentRef>(requestedMechDef.Inventory);
                //ReflectionHelper.InvokePrivateMethode(mechLabPanel.activeMechDef, "InsertFixedEquipmentIntoInventory", null);

                // Better as it calls RefreshInventory()? -> No, Tonnage is not adjusted... need to look into it somewhen
                //mechLabPanel.activeMechDef.SetInventory(requestedMechDef.Inventory);



                // Update dependent widgets (also calls CalculateCBillValue() -> CalculateSimGameWorkOrderCost() -> PruneWorkOrder())
                mechInfoWidget.RefreshInfo();

                // Mark as modified
                Traverse.Create(mechLabPanel).Field("Modified").SetValue(true);
                GameObject modifiedIcon = (GameObject)AccessTools.Field(typeof(MechLabPanel), "modifiedIcon").GetValue(mechLabPanel);
                modifiedIcon.SetActive(mechLabPanel.Modified);

                // Validate
                mechLabPanel.ValidateLoadout(true);
                ReflectionHelper.InvokePrivateMethode(mechLabPanel, "RefreshInventorySelectability", null);

                // Set Nickname
                mechNickname.SetText(requestedMechDef.Description.Name);

                // @ToDo: Inform user about what components were installed from where

            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }



        public static bool CanApplyLoadout(this MechLabPanel mechLabPanel, MechDef mechDef, out List<string> errorDescriptions)
        {
            bool result = false;
            errorDescriptions = new List<string>();

            MechLabInventoryWidget inventoryWidget = (MechLabInventoryWidget)AccessTools.Field(typeof(MechLabPanel), "inventoryWidget").GetValue(mechLabPanel);
            MechLabDismountWidget dismountWidget = (MechLabDismountWidget)AccessTools.Field(typeof(MechLabPanel), "dismountWidget").GetValue(mechLabPanel);

            List<MechComponentRef> activeMechInventory = mechLabPanel.activeMechInventory;
            MechComponentRef[] mechComponentsArray = (MechComponentRef[])AccessTools.Field(typeof(MechDef), "inventory").GetValue(mechDef);
            List<MechComponentRef> mechComponentsRequired = mechComponentsArray.ToList();

            // Remove fixed equipment as it will be ignored from dismounting et all
            for (int i = mechComponentsRequired.Count - 1; i >= 0; i--)
            {
                if (mechComponentsRequired[i].IsFixed)
                {
                    Logger.Debug("[Extensions.CanApplyLoadout] FOUND AND WILL REMOVE FIXED EQUIPMENT: " + mechComponentsRequired[i].ComponentDefID);
                    mechComponentsRequired.RemoveAt(i);
                }
            }

            // Check current inventory
            for (int i = mechComponentsRequired.Count - 1; i >= 0; i--)
            {
                for (int j = activeMechInventory.Count - 1; j >= 0; j--)
                {
                    if (mechComponentsRequired[i].ComponentDefID == activeMechInventory[j].ComponentDefID)
                    {
                        mechComponentsRequired.RemoveAt(i);
                        break;
                    }
                }
            }
            // Check dismount too (as command could also be triggered with already stripped equipment)
            for (int i = mechComponentsRequired.Count - 1; i >= 0; i--)
            {
                for (int j = dismountWidget.localInventory.Count - 1; j >= 0; j--)
                {
                    if (mechComponentsRequired[i].ComponentDefID == dismountWidget.localInventory[j].ComponentRef.ComponentDefID)
                    {
                        mechComponentsRequired.RemoveAt(i);
                        break;
                    }
                }
            }

            // Check storage for remaining required components
            List<InventoryItemElement_Simple> mechItemsRequired = Utilities.ComponentsToInventoryItems(mechComponentsRequired, true);
            List<InventoryItemElement_Simple> missingItems = new List<InventoryItemElement_Simple>();

            result = mechLabPanel.ItemsAvailableInInventory(mechItemsRequired, inventoryWidget.localInventory, out missingItems);

            foreach (InventoryItemElement_Simple item in missingItems)
            {
                Logger.Debug("[Extensions.CanApplyLoadout] missingItems: " + item.ComponentRef.ComponentDefID + " (Quantity: " + item.Quantity + ")");
                //errorDescriptions.Add(Environment.NewLine + "• Components missing: " + item.ComponentRef.ComponentDefID + " (Quantity: " + item.Quantity + ")");

                for (int i = 0; i < item.Quantity; i++)
                {
                    errorDescriptions.Add("• Missing " + item.ComponentRef.Def.ComponentType + ": " + item.ComponentRef.ComponentDefID);
                    /*
                    errorDescriptions.Add
                    (
                        Environment.NewLine +
                        "• Missing " +
                        item.ComponentRef.Def.ComponentType +
                        ": " +
                        item.ComponentRef.Def.Description.Manufacturer +
                        " " +
                        item.ComponentRef.Def.Description.Name +
                        " (" +
                        item.ComponentRef.ComponentDefID +
                        ")"
                    );
                    */
                }
            }

            return result;
        }



        public static bool ItemsAvailableInInventory(this MechLabPanel mechLabPanel, List<InventoryItemElement_Simple> requiredItems, List<InventoryItemElement_NotListView> requestedInventory)
        {
            foreach (InventoryItemElement_Simple requiredItem in requiredItems)
            {
                bool match = false;
                foreach (InventoryItemElement_NotListView inventoryItem in requestedInventory)
                {
                    if (inventoryItem.ComponentRef.ComponentDefID == requiredItem.ComponentRef.ComponentDefID)
                    {
                        Logger.Debug("[Extensions.InventoryHasAllItemsInStock] requestedInventory: " + inventoryItem.ComponentRef.ComponentDefID + " (Quantity: " + inventoryItem.controller.quantity + ")");
                        Logger.Debug("[Extensions.InventoryHasAllItemsInStock] requiredItems: " + requiredItem.ComponentRef.ComponentDefID + " (Quantity: " + requiredItem.Quantity + ")");
                        match = true;

                        // Need to check the controller as a potential quantity change via store is NOT reflected properly in vanilla
                        //if (inventoryItem.Quantity >= requiredItem.Quantity)
                        if (inventoryItem.controller.quantity >= requiredItem.Quantity)
                        {
                            break;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                if (!match)
                {
                    return false;
                }
            }
            return true;
        }



        public static bool ItemsAvailableInInventory(this MechLabPanel mechLabPanel, List<InventoryItemElement_Simple> requiredItems, List<InventoryItemElement_NotListView> requestedInventory, out List<InventoryItemElement_Simple> missingItems)
        {
            missingItems = new List<InventoryItemElement_Simple>();
            bool result = true;

            foreach (InventoryItemElement_Simple requiredItem in requiredItems)
            {
                bool match = false;
                foreach (InventoryItemElement_NotListView inventoryItem in requestedInventory)
                {
                    if (inventoryItem.ComponentRef.ComponentDefID == requiredItem.ComponentRef.ComponentDefID)
                    {
                        Logger.Debug("[Extensions.InventoryHasAllItemsInStock] requestedInventory: " + inventoryItem.ComponentRef.ComponentDefID + " (Quantity: " + inventoryItem.controller.quantity + ")");
                        Logger.Debug("[Extensions.InventoryHasAllItemsInStock] requiredItems: " + requiredItem.ComponentRef.ComponentDefID + " (Quantity: " + requiredItem.Quantity + ")");
                        match = true;

                        // Need to check the controller as a potential quantity change via store is NOT reflected properly in vanilla
                        //if (inventoryItem.Quantity >= requiredItem.Quantity)
                        if (inventoryItem.controller.quantity >= requiredItem.Quantity)
                        {
                            break;
                        }
                        else
                        {
                            //return false;

                            Logger.Debug("[Extensions.InventoryHasAllItemsInStock] requestedInventory is missing some: " + requiredItem.ComponentRef.ComponentDefID + " (" + (requiredItem.Quantity - inventoryItem.controller.quantity) + ")");

                            InventoryItemElement_Simple missingItem = new InventoryItemElement_Simple
                            {
                                ComponentRef = requiredItem.ComponentRef,
                                Quantity = requiredItem.Quantity - inventoryItem.controller.quantity
                            };
                            missingItems.Add(missingItem);

                            result = false;
                        }
                    }
                }
                if (!match)
                {
                    //return false;

                    Logger.Debug("[Extensions.InventoryHasAllItemsInStock] requestedInventory is missing all: " + requiredItem.ComponentRef.ComponentDefID + " (" + requiredItem.Quantity + ")");

                    InventoryItemElement_Simple missingItem = new InventoryItemElement_Simple
                    {
                        ComponentRef = requiredItem.ComponentRef,
                        Quantity = requiredItem.Quantity
                    };
                    missingItems.Add(missingItem);

                    result = false;
                }
            }
            return result;
        }



        public static List<InventoryItemElement_NotListView> PullItemsFromInventory(this MechLabPanel mechLabPanel, List<InventoryItemElement_Simple> requiredItems, List<InventoryItemElement_NotListView> requestedInventory)
        {
            MechLabInventoryWidget inventoryWidget = (MechLabInventoryWidget)AccessTools.Field(typeof(MechLabPanel), "inventoryWidget").GetValue(mechLabPanel);
            List<InventoryItemElement_NotListView> collectedItems = new List<InventoryItemElement_NotListView>();

            // Reverse iterate to be able to directly remove without exception
            // BEWARE: Throws exception if the inventory gets completely emptied during this process (very unlikely but possible)
            for (int i = requestedInventory.Count - 1; i >= 0; i--)
            {
                foreach (InventoryItemElement_Simple requiredItem in requiredItems)
                {
                    if (requestedInventory[i].ComponentRef.ComponentDefID == requiredItem.ComponentRef.ComponentDefID)
                    {
                        //if (requestedInventory[i].Quantity >= requiredItem.Quantity)
                        if (requestedInventory[i].controller.quantity >= requiredItem.Quantity)
                        {
                            for (int j = 0; j < requiredItem.Quantity; j++)
                            {
                                // Collect before removal just makes sense... :-)
                                collectedItems.Add(requestedInventory[i]);
                                inventoryWidget.RemoveItem(requestedInventory[i]);
                            }
                        }
                    }
                }
            }
            return collectedItems;
        }



        public static void SetArmor(this MechLabPanel mechLabPanel, MechLabLocationWidget locationWidget, LocationLoadoutDef loadout)
        {
            locationWidget.currentArmor = loadout.AssignedArmor;
            bool locationHasRearArmor = (bool)AccessTools.Field(typeof(MechLabLocationWidget), "useRearArmor").GetValue(locationWidget);

            if (locationHasRearArmor)
            {
                locationWidget.currentRearArmor = loadout.AssignedRearArmor;
            }

            // Create work-order, otherwise Mech is invalid after it is actually changed
            // Check if the values actually changed beforehand (Vanilla does actually NOT check this)
            int armorDiff = (int)Mathf.Abs(locationWidget.currentArmor - locationWidget.originalArmor) + (int)Mathf.Abs(locationWidget.currentRearArmor - locationWidget.originalRearArmor);
            if (armorDiff != 0)
            {
                WorkOrderEntry_ModifyMechArmor subEntry = locationWidget.Sim.CreateMechArmorModifyWorkOrder(mechLabPanel.activeMechDef.GUID, locationWidget.loadout.Location, armorDiff, (int)locationWidget.currentArmor, (int)locationWidget.currentRearArmor);
                mechLabPanel.baseWorkOrder.AddSubEntry(subEntry);
            }
            //mechLabPanel.ValidateLoadout(false); 

            ReflectionHelper.InvokePrivateMethode(locationWidget, "RefreshArmor", null);
        }



        // @ToDo: Somehow implement that manual Remove/Add of components AFTER this method-call calculates work-orders correctly
        public static void SetEquipment(this MechLabPanel mechLabPanel, List<InventoryItemElement_Simple> equipment, MechLabLocationWidget locationWidget, LocationLoadoutDef loadout)
        {
            locationWidget.loadout = loadout;

            // Nah. This kills fixed equipment!
            //locationWidget.ClearInventory();

            for (int i = 0; i < equipment.Count; i++)
            {
                if (equipment[i].ComponentRef.MountedLocation == loadout.Location)
                {
                    Logger.Debug("[Extensions.SetEquipment] Component: " + equipment[i].ComponentRef.ComponentDefID + " gets installed at: " + loadout.Location.ToString() + " and was fetched from (simulated): " + equipment[i].Origin);

                    // NOTE that creation of items (vs. simulate-grabbing the real ones from inventory) will confuse the work-order-entries
                    // @ToDo: Try setting "copyComponentRef" to true and test somewhen
                    MechLabItemSlotElement mechLabItemSlotElement = mechLabPanel.CreateMechComponentItem(equipment[i].ComponentRef, false, loadout.Location, locationWidget, null);
                    Traverse.Create(mechLabItemSlotElement).Field("originalDropParentType").SetValue(equipment[i].Origin);

                    if (mechLabItemSlotElement != null)
                    {
                        List<MechLabItemSlotElement> ___localInventory = (List<MechLabItemSlotElement>)AccessTools.Field(typeof(MechLabLocationWidget), "localInventory").GetValue(locationWidget);
                        ___localInventory.Add(mechLabItemSlotElement);
                        int ___usedSlots = (int)AccessTools.Field(typeof(MechLabLocationWidget), "usedSlots").GetValue(locationWidget);
                        ___usedSlots += equipment[i].ComponentRef.Def.InventorySize;
                        Transform ___inventoryParent = (Transform)AccessTools.Field(typeof(MechLabLocationWidget), "inventoryParent").GetValue(locationWidget);
                        mechLabItemSlotElement.gameObject.transform.SetParent(___inventoryParent, false);
                        mechLabItemSlotElement.gameObject.transform.localScale = Vector3.one;

                        ReflectionHelper.InvokePrivateMethode(locationWidget, "RefreshMechComponentData", new object[] { mechLabItemSlotElement, false });
                        locationWidget.RefreshHardpointData();

                        // Add WorkOrderEntry 
                        WorkOrderEntry_InstallComponent subEntry = locationWidget.Sim.CreateComponentInstallWorkOrder(mechLabPanel.baseWorkOrder.MechID, equipment[i].ComponentRef, loadout.Location, ChassisLocations.None);
                        mechLabPanel.baseWorkOrder.AddSubEntry(subEntry);
                    }
                }
            }
            //mechLabPanel.ValidateLoadout(false);
        }



        public static MechDef GetMechDefFromVariantName(this MechLabPanel mechLabPanel, string variant)
        {
            foreach (KeyValuePair<string, ChassisDef> chassisDefs in mechLabPanel.Sim.DataManager.ChassisDefs)
            {
                string chassisId = chassisDefs.Key;
                ChassisDef chassisDef = chassisDefs.Value;

                if (chassisDef.VariantName == variant || chassisDef.VariantName.ToUpper() == variant)
                {
                    string mechDefId = chassisDef.Description.Id.Replace("chassisdef", "mechdef");
                    Logger.Debug("[MechLabPanelExtensions_GetMechDefFromVariantName] Found MechDef: " + mechDefId);
                    MechDef mechDef = mechLabPanel.Sim.DataManager.MechDefs.Get(mechDefId);

                    return mechDef;
                }
            }
            Logger.Debug("[MechLabPanelExtensions_GetMechDefFromVariantName] No MechDef found for VariantName: " + variant);
            return null;
        }



        public static void ValidateAllMechDefTonnages(this MechLabPanel mechLabPanel, bool errorsOnly = true)
        {
            foreach (KeyValuePair<string, MechDef> mechDefs in mechLabPanel.Sim.DataManager.MechDefs)
            {
                string id = mechDefs.Key;
                MechDef mechDef = mechDefs.Value;
                float num = 0f;
                float tonnage = mechDef.Chassis.Tonnage;

                MechStatisticsRules.CalculateTonnage(mechDef, ref num, ref tonnage);

                if ((double)Mathf.Abs(num - mechDef.Chassis.Tonnage) < 0.0001)
                {
                    if (!errorsOnly)
                    {
                        Logger.Debug("[MechLabPanelExtensions_ValidateAllMechDefTonnages] MechDef (" + mechDef.Name + "/" + id + "): Passed");
                    }
                }
                else if (num > mechDef.Chassis.Tonnage)
                {
                    float diff = num - mechDef.Chassis.Tonnage;
                    Logger.Debug("[MechLabPanelExtensions_ValidateAllMechDefTonnages] MechDef (" + mechDef.Name + "/" + id + "): OVERWEIGHT (+" + diff + " Tons)");
                }
                //else if (num <= mechDef.Chassis.Tonnage - 0.5f)
                else if (num < mechDef.Chassis.Tonnage)
                {
                    float diff = mechDef.Chassis.Tonnage - num;
                    Logger.Debug("[MechLabPanelExtensions_ValidateAllMechDefTonnages] MechDef (" + mechDef.Name + "/" + id + "): UNDERWEIGHT (-" + diff + " Tons)");
                }
            }
        }



        public static void ExportCurrentMechDefToJson(this MechLabPanel mechLabPanel, string mechDefId, string mechDefName)
        {
            try
            {
                MechDef mechDef = new MechDef(mechLabPanel.activeMechDef, null, true);
                MechDef baseMechDef = new MechDef(mechLabPanel.Sim.DataManager.MechDefs.Get(mechDef.Description.Id), null, false);
                MechComponentRef[] mechDefInventory = (MechComponentRef[])AccessTools.Field(typeof(MechDef), "inventory").GetValue(mechDef);

                // Remove fixed equipment as it will be ignored from dismounting et all
                MechComponentRef[] mechDefInventoryFiltered = mechDefInventory.Where(component => component.IsFixed != true).ToArray();
                AccessTools.Field(typeof(MechDef), "inventory").SetValue(mechDef, mechDefInventoryFiltered);

                // Try to fix HardpointSlots the KISS way
                foreach (ChassisLocations chassisLocation in Enum.GetValues(typeof(ChassisLocations)))
                {
                    MechComponentRef[] mechDefWeaponsAtLocation = mechDefInventoryFiltered
                    .Where(component => component.MountedLocation == chassisLocation)
                    .Where(component => component.ComponentDefType == ComponentType.Weapon)
                    .ToArray();

                    for (int i = 0; i < mechDefWeaponsAtLocation.Length; i++)
                    {
                        new Traverse(mechDefWeaponsAtLocation[i]).Property("HardpointSlot").SetValue(i);
                        Logger.Debug("[Extensions.ExportCurrentMechDefToJson] (" + chassisLocation + ") (" + mechDefWeaponsAtLocation[i].ComponentDefID + ") HardpointSlot: " + mechDefWeaponsAtLocation[i].HardpointSlot);
                    }
                }

                // Tag MechDef according to threatLevel/equipment
                //string additionalTag = Utilities.TagMechDefAccordingToInventory(mechDefInventory);
                int threatLevel = Utilities.GetExtraThreatLevelFromMechDef(mechDef, true);
                string additionalTag = Utilities.GetMechTagForThreatLevel(threatLevel);


                Logger.Debug("[Extensions.ExportCurrentMechDefToJson] additionalTag: " + additionalTag);

                // Set some halfway correct value for part value
                int simGameMechPartCost = mechDef.SimGameMechPartCost > 0 ? mechDef.SimGameMechPartCost : (mechDef.BattleValue / 10);

                string baseDirectory = $"{ MadLabs.ModDirectory}";
                string filePath = Path.Combine(Path.Combine(baseDirectory, "MechDefs"), $"{mechDefId}.json");
                Directory.CreateDirectory(Directory.GetParent(filePath).FullName);



                using (StreamWriter streamWriter = new StreamWriter(filePath, false))
                //using (JsonTextWriter jsonTextWriter = new JsonTextWriter(streamWriter) { Formatting = Formatting.Indented, Indentation = 4, IndentChar = ' ' })
                {
                    string mechDefJson = mechDef.ToJSON();
                    TagSet mechTags = new TagSet(baseMechDef.MechTags);
                    mechTags.Add("unit_madlabs");
                    mechTags.Add(additionalTag);

                    if (MadLabs.Settings.BlacklistExportedMechDefs)
                    {
                        mechTags.Add("BLACKLISTED");
                    }

                    string mechTagsJson = mechTags.ToJSON();

                    // Fix MechDefJson
                    JObject jMechDef = JObject.Parse(mechDefJson);
                    JObject jBaseMechDefMechTagsJson = JObject.Parse(mechTagsJson);

                    jMechDef.Property("Chassis").Remove();
                    jMechDef.Property("PaintTextureID").Remove();
                    jMechDef.Property("HeraldryID").Remove();
                    jMechDef.Property("simGameMechPartCost").Remove();
                    jMechDef.Property("prefabOverride").Remove();

                    jMechDef["MechTags"] = jBaseMechDefMechTagsJson;

                    JObject jDescription = (JObject)jMechDef["Description"];
                    // Raise rarity?
                    jDescription["Rarity"] = (int)(mechDef.Description.Rarity + 4);

                    jDescription["Manufacturer"] = null;
                    jDescription["Model"] = null;
                    jDescription["Name"] = mechDefName;
                    jDescription["Id"] = mechDefId;


                    jMechDef.Property("ChassisID").AddAfterSelf(new JProperty("HeraldryID", null));
                    jMechDef.Property("Description").AddAfterSelf(new JProperty("simGameMechPartCost", simGameMechPartCost));
                    jMechDef["Version"] = 1;

                    JArray jInventory = (JArray)jMechDef["inventory"];
                    foreach (JObject jComponent in jInventory)
                    {
                        jComponent["SimGameUID"] = null;
                        jComponent.Property("IsFixed").Remove();
                        jComponent["prefabName"] = null;
                        jComponent["hasPrefabName"] = false;
                    }



                    streamWriter.Write(jMechDef.ToString());
                    //jMechDef.WriteTo(jsonTextWriter);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
