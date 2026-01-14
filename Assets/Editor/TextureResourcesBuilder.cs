#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;

/// <summary>
/// Automatically copies HighResTextures to Resources folder before building
/// This ensures textures are available at runtime in builds
/// </summary>
public class TextureResourcesBuilder : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    
    private const string SOURCE_FOLDER = "Assets/HighResTextures";
    private const string RESOURCES_FOLDER = "Assets/Resources/HighResTextures";

    public void OnPreprocessBuild(BuildReport report)
    {
        Debug.Log("=== TextureResourcesBuilder: Preparing textures for build ===");
        
        // Create Resources folder structure if it doesn't exist
        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
            Debug.Log("Created Assets/Resources folder");
        }
        
        // Copy textures to Resources folder
        if (Directory.Exists(SOURCE_FOLDER))
        {
            CopyTexturesRecursively(SOURCE_FOLDER, RESOURCES_FOLDER);
            AssetDatabase.Refresh();
            Debug.Log("✓ Textures copied to Resources folder for build");
        }
        else
        {
            Debug.LogWarning($"Source texture folder not found: {SOURCE_FOLDER}");
        }
        
        Debug.Log("=== TextureResourcesBuilder: Build preprocessing complete ===");
    }
    
    /// <summary>
    /// Recursively copies textures from source to destination
    /// </summary>
    private void CopyTexturesRecursively(string sourcePath, string destPath)
    {
        // Create destination directory
        if (!Directory.Exists(destPath))
        {
            Directory.CreateDirectory(destPath);
        }
        
        // Copy all files
        string[] files = Directory.GetFiles(sourcePath);
        foreach (string file in files)
        {
            // Skip .meta files
            if (file.EndsWith(".meta"))
                continue;
            
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destPath, fileName);
            
            // Only copy if source is newer or destination doesn't exist
            if (!File.Exists(destFile) || File.GetLastWriteTime(file) > File.GetLastWriteTime(destFile))
            {
                File.Copy(file, destFile, true);
                Debug.Log($"  Copied: {fileName}");
            }
        }
        
        // Recursively copy subdirectories
        string[] directories = Directory.GetDirectories(sourcePath);
        foreach (string directory in directories)
        {
            string dirName = Path.GetFileName(directory);
            string destDir = Path.Combine(destPath, dirName);
            CopyTexturesRecursively(directory, destDir);
        }
    }
    
    /// <summary>
    /// Menu item to manually copy textures to Resources
    /// </summary>
    [MenuItem("Tools/Planet Textures/Copy to Resources Folder")]
    public static void ManualCopyToResources()
    {
        var builder = new TextureResourcesBuilder();
        
        if (Directory.Exists(SOURCE_FOLDER))
        {
            builder.CopyTexturesRecursively(SOURCE_FOLDER, RESOURCES_FOLDER);
            AssetDatabase.Refresh();
            Debug.Log("✓ Manually copied textures to Resources folder");
            EditorUtility.DisplayDialog("Success", 
                "Textures copied to Resources folder successfully!", "OK");
        }
        else
        {
            Debug.LogError($"Source folder not found: {SOURCE_FOLDER}");
            EditorUtility.DisplayDialog("Error", 
                $"Source folder not found: {SOURCE_FOLDER}", "OK");
        }
    }
    
    /// <summary>
    /// Menu item to clean Resources folder
    /// </summary>
    [MenuItem("Tools/Planet Textures/Clean Resources Folder")]
    public static void CleanResourcesFolder()
    {
        if (Directory.Exists(RESOURCES_FOLDER))
        {
            Directory.Delete(RESOURCES_FOLDER, true);
            
            // Also delete the .meta file
            string metaFile = RESOURCES_FOLDER + ".meta";
            if (File.Exists(metaFile))
                File.Delete(metaFile);
            
            AssetDatabase.Refresh();
            Debug.Log("✓ Cleaned Resources/HighResTextures folder");
            EditorUtility.DisplayDialog("Success", 
                "Resources folder cleaned successfully!", "OK");
        }
        else
        {
            Debug.Log("Resources/HighResTextures folder doesn't exist");
        }
    }
}
#endif
