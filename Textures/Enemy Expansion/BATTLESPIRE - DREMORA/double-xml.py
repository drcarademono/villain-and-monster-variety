import xml.etree.ElementTree as ET
import glob

# Pattern to match all XML files in the current folder
xml_files_pattern = './*.xml'

def set_scale_values_to_half(xml_file):
    # Parse the XML file
    tree = ET.parse(xml_file)
    root = tree.getroot()
    
    # Set the scaleX and scaleY elements to 0.5
    for scale_element in root.findall('.//scaleX'):
        scale_element.text = '0.5328'
        
    for scale_element in root.findall('.//scaleY'):
        scale_element.text = '0.5328'
    
    # Write the modified XML back to the file
    tree.write(xml_file)

# Iterate over all XML files in the current folder and apply the changes
for xml_file in glob.glob(xml_files_pattern):
    set_scale_values_to_half(xml_file)
    print(f"Processed {xml_file}")

