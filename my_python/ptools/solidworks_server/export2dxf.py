import win32com.client
import pythoncom
import os
import subprocess
from pathlib import Path

def force_unfold(SwApp=None):
    """
    Force unfold SolidWorks part flat pattern view and export to DXF file
    Save DXF file to the same folder as the document
    """
    try:
        # If no SwApp instance is passed, create a new connection
        if SwApp is None:
            pythoncom.CoInitialize()
            SwApp = win32com.client.Dispatch("SldWorks.Application")
        
        # Get active document
        swModel = SwApp.ActiveDoc
        if not swModel:
            print("No document is open")
            return False
        
        # Check if it's a part document (1 represents part document)
        if swModel.GetType != 1:
            print("Current document is not a part document")
            return False
        
        # Convert to part document object
        swPart = swModel
        
        # Get document path
        doc_path = swModel.GetPathName
        if not doc_path:
            print("Document is not saved, cannot determine save location")
            return False
        
        # Get document directory
        doc_directory = os.path.dirname(doc_path)
        print(f"Document directory: {doc_directory}")
        # Export flat pattern view to DXF in the same directory
        success = swPart.ExportFlatPatternView(doc_directory, 1)
        
        if not success:
            print("Failed to export flat pattern view")
            return False
        else:
            # Open the document directory
            subprocess.Popen(f'explorer "{doc_directory}"')
            print(f"Flat pattern view exported to: {doc_directory}")
            return True
            
    except Exception as e:
        print(f"Error during execution: {e}")
        return False
    
    finally:
        # Clean up COM resources if it's a new connection
        if SwApp is None:
            pythoncom.CoUninitialize()

def get_document_directory():
    """
    Get the directory of the currently opened SolidWorks document
    """
    try:
        pythoncom.CoInitialize()
        SwApp = win32com.client.Dispatch("SldWorks.Application")
        swModel = SwApp.ActiveDoc
        
        if swModel and swModel.GetPathName:
            doc_path = swModel.GetPathName
            doc_directory = os.path.dirname(doc_path)
            print(f"Document directory: {doc_directory}")
            return doc_directory
        else:
            print("No saved document found")
            return None
    except Exception as e:
        print(f"Error getting document directory: {e}")
        return None
    finally:
        pythoncom.CoUninitialize()

# Usage example
if __name__ == "__main__":
    # Method 1: Use existing SolidWorks instance
    # force_unfold()
    
    # Method 2: Pass specific SolidWorks application instance
    # pythoncom.CoInitialize()
    # SwApp = win32com.client.Dispatch("SldWorks.Application")
    # force_unfold(SwApp)
    # pythoncom.CoUninitialize()
    
    # Simple test call
    result = force_unfold()
    if result:
        print("Flat export successful")
    else:
        print("Flat export failed")