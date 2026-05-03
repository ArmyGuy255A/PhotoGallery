#!/usr/bin/env python3
"""Upload sample photos and check if they appear in MinIO"""
import requests
import json
import os
import sys

# Configuration
BACKEND_URL = 'http://localhost:5105'
ALBUM_ID = 'c373c777-c47a-4648-937e-c92006319c30'  # Use existing test album
SAMPLE_PHOTOS_DIR = r'D:\repos\PhotoGallery\PhotoGallery\SamplePhotos'

print("=" * 60)
print("PHOTO UPLOAD DIAGNOSTIC")
print("=" * 60)

# Get list of sample photos
photos = []
if os.path.exists(SAMPLE_PHOTOS_DIR):
    photos = [f for f in os.listdir(SAMPLE_PHOTOS_DIR) if f.endswith('.jpg')]
    print(f"\n✓ Found {len(photos)} sample photos:")
    for p in photos:
        size = os.path.getsize(os.path.join(SAMPLE_PHOTOS_DIR, p))
        print(f"  - {p} ({size} bytes)")
else:
    print(f"\n✗ Sample photos directory not found: {SAMPLE_PHOTOS_DIR}")
    sys.exit(1)

# Upload first photo with detailed logging
if photos:
    photo_file = photos[0]
    photo_path = os.path.join(SAMPLE_PHOTOS_DIR, photo_file)
    
    print(f"\n1. Uploading first photo: {photo_file}")
    
    with open(photo_path, 'rb') as f:
        files = {'files': (photo_file, f, 'image/jpeg')}
        
        try:
            url = f'{BACKEND_URL}/api/photos/albums/{ALBUM_ID}'
            print(f"   URL: POST {url}")
            print(f"   File size: {os.path.getsize(photo_path)} bytes")
            
            response = requests.post(url, files=files, timeout=10)
            
            print(f"\n2. Response:")
            print(f"   Status Code: {response.status_code}")
            print(f"   Content-Type: {response.headers.get('content-type')}")
            
            if response.status_code == 200:
                data = response.json()
                print(f"   Response Body: {json.dumps(data, indent=2)}")
                
                if data.get('totalUploaded', 0) > 0:
                    print(f"\n✓ Upload successful!")
                    print(f"  - Total Uploaded: {data['totalUploaded']}")
                    if data.get('successfulUploads'):
                        for upload in data['successfulUploads']:
                            print(f"  - Photo ID: {upload['photoId']}")
                            print(f"  - Processing Job ID: {upload['processingJobId']}")
                else:
                    print(f"\n✗ Upload failed!")
                    if data.get('errors'):
                        for error in data['errors']:
                            print(f"  - Error: {error}")
            else:
                print(f"   Raw Response: {response.text[:500]}")
                print(f"\n✗ Upload failed with status {response.status_code}")
                
        except Exception as e:
            print(f"\n✗ Exception during upload: {e}")
            import traceback
            traceback.print_exc()

# Check if photos appear in the API
print(f"\n3. Checking if photos appear in API:")
try:
    url = f'{BACKEND_URL}/api/albums/{ALBUM_ID}/photos'
    response = requests.get(url, timeout=5)
    if response.status_code == 200:
        photos_data = response.json()
        print(f"   Status: {response.status_code}")
        print(f"   Photos in album: {len(photos_data)}")
        for photo in photos_data:
            print(f"   - {photo.get('fileName')} (ID: {photo.get('id')})")
    else:
        print(f"   ✗ Failed to get photos: {response.status_code}")
except Exception as e:
    print(f"   ✗ Exception: {e}")

print("\n" + "=" * 60)
