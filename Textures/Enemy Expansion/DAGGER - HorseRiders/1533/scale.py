import os
import xml.etree.ElementTree as ET

# Loop through all files in the current directory
for filename in os.listdir('.'):
    if filename.endswith('.xml'):
        # Parse the XML file
        tree = ET.parse(filename)
        root = tree.getroot()

        # Find and update scaleX and scaleY values
        for scale in root.findall(".//scaleX") + root.findall(".//scaleY"):
            if scale.text == '0.52':
                scale.text = '0.6'

        # Save the updated XML file
        tree.write(filename)

print("All '0.65' values have been replaced with '0.975'.")

