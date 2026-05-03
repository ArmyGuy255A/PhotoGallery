#!/usr/bin/env python3
"""Verify uploaded photos appear in album"""

from playwright.sync_api import sync_playwright
import time
import os

def verify_photo_persistence():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        
        print("🎬 Verifying photo persistence...")
        
        try:
            # Navigate to dashboard
            print("\n1️⃣  Loading dashboard...")
            page.goto('http://localhost:4200/dashboard')
            page.wait_for_load_state('networkidle')
            time.sleep(1)
            
            # Get initial album count
            initial_albums = page.locator('.album-card').count()
            print(f"2️⃣  Albums on dashboard: {initial_albums}")
            
            # Open first album
            album_card = page.locator('.album-card').first
            album_title = album_card.locator('h3').text_content()
            print(f"3️⃣  Opening album: {album_title}")
            
            album_card.locator('text="View Album"').click()
            page.wait_for_load_state('networkidle')
            time.sleep(1)
            
            # Check initial photo count
            photos_heading = page.locator('h2:has-text("Photos")').first
            initial_photos_text = photos_heading.text_content() if photos_heading.count() > 0 else "Photos (0)"
            print(f"4️⃣  Initial photos: {initial_photos_text}")
            
            # Upload a photo
            file_input = page.locator('input[type="file"]').first
            sample_photo_dir = r"D:\repos\PhotoGallery\PhotoGallery\SamplePhotos"
            
            if os.path.exists(sample_photo_dir):
                photos = [f for f in os.listdir(sample_photo_dir) 
                         if f.lower().endswith(('.jpg', '.jpeg', '.png', '.cr2', '.raw'))]
                
                if photos:
                    photo_file = os.path.join(sample_photo_dir, photos[0])
                    print(f"5️⃣  Uploading: {photos[0]}")
                    
                    file_input.set_input_files(photo_file)
                    print("    ⏳ Waiting for upload to complete...")
                    time.sleep(4)
                    
                    page.wait_for_load_state('networkidle')
                    time.sleep(1)
                    
                    # Take screenshot after upload
                    page.screenshot(path='/tmp/after-upload-persistence.png')
                    
                    # Check photo count again
                    photos_heading = page.locator('h2:has-text("Photos")').first
                    after_photos_text = photos_heading.text_content() if photos_heading.count() > 0 else "Photos (0)"
                    print(f"6️⃣  After upload: {after_photos_text}")
                    
                    # Check photo cards
                    photo_cards = page.locator('.photo-card')
                    print(f"7️⃣  Photo cards visible: {photo_cards.count()}")
                    
                    # Check for status badges
                    status_badges = page.locator('.photo-status-badge')
                    print(f"8️⃣  Status badges: {status_badges.count()}")
                    
                    # Get status badge colors
                    for i in range(min(3, status_badges.count())):
                        badge = status_badges.nth(i)
                        class_name = badge.get_attribute('class')
                        print(f"     - Badge {i}: {class_name}")
                    
                    # Refresh page to see if data persists
                    print(f"\n9️⃣  Refreshing page to verify persistence...")
                    page.reload()
                    page.wait_for_load_state('networkidle')
                    time.sleep(1)
                    
                    photos_heading = page.locator('h2:has-text("Photos")').first
                    refreshed_photos_text = photos_heading.text_content() if photos_heading.count() > 0 else "Photos (0)"
                    print(f"🔟  After refresh: {refreshed_photos_text}")
                    
                    photo_cards = page.locator('.photo-card')
                    print(f"    Photo cards: {photo_cards.count()}")
                    
                    print("\n✅ Photo persistence test complete!")
                else:
                    print("❌ No sample photos found")
            else:
                print(f"❌ Sample directory not found")
            
            browser.close()
            return True
            
        except Exception as e:
            print(f"❌ Error: {e}")
            browser.close()
            return False

if __name__ == '__main__':
    verify_photo_persistence()
