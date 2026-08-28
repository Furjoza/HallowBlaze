using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class ManageRecordsOfflineTests
{
    [Test]
    public void Test_OfflineMode_InputFieldHandlerWorks()
    {
        // Arrange
        Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
        Type manageRecordsType = gameAssembly.GetType("ManageRecords");
        Assert.IsNotNull(manageRecordsType, "ManageRecords type should be found");

        var go = new GameObject("TestManageRecords");
        var manageRecords = go.AddComponent(manageRecordsType);
        var behaviour = (Behaviour)manageRecords;
        behaviour.enabled = false; // Disable to prevent OnEnable from registering listeners

        // Set up text components via reflection
        var resultsTextGO = new GameObject("TestResultsText");
        var resultsTextComponent = resultsTextGO.AddComponent<Text>();
        FieldInfo resultsTextField = manageRecordsType.GetField("resultsText", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(resultsTextField, "resultsText field should exist");
        resultsTextField.SetValue(manageRecords, resultsTextComponent);

        var uploadedRecordTextGO = new GameObject("TestUploadedRecordText");
        var uploadedRecordTextComponent = uploadedRecordTextGO.AddComponent<Text>();
        FieldInfo uploadedRecordTextField = manageRecordsType.GetField("uploadedRecordText", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(uploadedRecordTextField, "uploadedRecordText field should exist");
        uploadedRecordTextField.SetValue(manageRecords, uploadedRecordTextComponent);

        // Set up input field
        var inputFieldGO = new GameObject("TestInputField");
        var inputFieldComponent = inputFieldGO.AddComponent<InputField>();
        FieldInfo inputField = manageRecordsType.GetField("mainInputField", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(inputField, "mainInputField field should exist");
        inputField.SetValue(manageRecords, inputFieldComponent);

        // Set up button
        var buttonObject = new GameObject("TestButton");
        var buttonComponent = buttonObject.AddComponent<Button>();
        FieldInfo uploadButton = manageRecordsType.GetField("uploadRecordButton", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(uploadButton, "uploadRecordButton field should exist");
        uploadButton.SetValue(manageRecords, buttonComponent);

        try
        {
            // Enable the component to register listeners properly
            behaviour.enabled = true;

            // Call LoadScores() via reflection first to ensure proper initialization
            MethodInfo loadScoresMethod = manageRecordsType.GetMethod("LoadScores", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(loadScoresMethod, "LoadScores method should exist");
            loadScoresMethod.Invoke(manageRecords, null);

            // Verify button is inactive after LoadScores call (should be disabled in offline mode)
            Assert.IsFalse(buttonObject.activeSelf, "Upload button should be inactive after LoadScores");

            // Verify IsUploadAvailable returns false
            MethodInfo isUploadAvailableMethod = manageRecordsType.GetMethod("IsUploadAvailable", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(isUploadAvailableMethod, "IsUploadAvailable method should exist");
            bool isAvailable = (bool)isUploadAvailableMethod.Invoke(manageRecords, null);
            Assert.IsFalse(isAvailable, "Upload should not be available in offline mode");

            // Simulate user input with name containing / and | characters
            inputFieldComponent.onEndEdit.Invoke("Test/Player|Name");

            // Verify results text contains expected content (without / and |)
            Assert.IsTrue(resultsTextComponent.text.Contains("unavailable"), "Results should show offline message");
            Assert.IsTrue(resultsTextComponent.text.Contains("TestPlayerName"), "Results should contain cleaned name without / and |");
            Assert.IsFalse(resultsTextComponent.text.Contains("/"), "Results should not contain forward slash");
            Assert.IsFalse(resultsTextComponent.text.Contains("|"), "Results should not contain pipe character");

            // Verify uploaded record text contains expected content
            Assert.IsTrue(uploadedRecordTextComponent.text.Contains("TestPlayerName"), "Uploaded record text should contain cleaned name");
            Assert.IsTrue(uploadedRecordTextComponent.text.Contains("local record is"), "Uploaded record text should show local score information");
        }
        finally
        {
            // Clean up - disable behaviour and destroy GameObjects
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(resultsTextGO);
            UnityEngine.Object.DestroyImmediate(uploadedRecordTextGO);
            UnityEngine.Object.DestroyImmediate(inputFieldGO);
            UnityEngine.Object.DestroyImmediate(buttonObject);
        }
    }
}