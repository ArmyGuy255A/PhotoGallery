#!/usr/bin/env python3
"""Inspect the album detail page to debug upload component"""
from playwright.sync_api import sync_playwright
import time

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page()
    
    try:
        print("Loading album detail page...")
        page.goto('http://localhost:4200', wait_until='networkidle')
        time.sleep(1)
        
        # Click first album
        albums = page.locator('text=View Album')
        if albums.count() > 0:
            albums.first.click()
            page.wait_for_load_state('networkidle')
            time.sleep(2)
        
        # Get page content
        content = page.content()
        
        # Look for upload-related elements
        print("\nSearching for upload elements...")
        
        if 'photo-upload' in content:
            print("✓ photo-upload component found in HTML")
        else:
            print("✗ photo-upload component NOT in HTML")
        
        if '<input type="file"' in content:
            print("✓ File input found")
        else:
            print("✗ File input NOT found")
        
        if 'drag' in content.lower():
            print("✓ Drag-drop references found")
        else:
            print("✗ No drag-drop references")
        
        # Save screenshot and HTML for inspection
        page.screenshot(path='album_detail_inspect.png', full_page=True)
        with open('album_detail.html', 'w') as f:
            f.write(content)
        
        print("\n✓ Saved album_detail.html and album_detail_inspect.png for inspection")
        
        # Check for any errors in console
        print("\nConsole messages:")
        page.evaluate('() => console.log("Page inspection complete")')
        
    finally:
        browser.close()
