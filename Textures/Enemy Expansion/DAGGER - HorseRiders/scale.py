import os
import xml.etree.ElementTree as ET

def update_scales_in_file(filepath):
    # Parse the XML file
    tree = ET.parse(filepath)
    root = tree.getroot()
    
    # Find and update scaleX and scaleY values
    for scale in root.findall(".//scaleX") + root.findall(".//scaleY"):
        if scale.text == '0.52':
            scale.text = '0.6'

    # Save the updated XML file
    tree.write(filepath)

def process_directory(directory):
    for root, _, files in os.walk(directory):
        for file in files:
            if file.endswith('.xml'):
                file_path = os.path.join(root, file)
                update_scales_in_file(file_path)

# Run the script starting from the current directory
process_directory('.')

print("All scale values have been multiplied by 0.8.")

