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
    return doc_directory

# Usage example
if __name__ == "__main__":

    result = force_unfold()
