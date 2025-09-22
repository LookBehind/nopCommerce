# Company & Delivery Management Plugin

## Overview

This enhanced NopCommerce plugin provides comprehensive company management and global delivery date picker functionality specifically designed for lunch delivery services. It replaces the traditional checkout-based delivery time selection with a modern, user-friendly global header picker.

## Features

### 🏢 Company Management
- Manage company information and settings
- Company-specific timezone support
- Customer and vendor associations

### 📅 Global Delivery Date Picker
- **Header Integration**: Delivery time selector appears in the website header alongside language/currency selectors
- **Smart Time Restrictions**: Automatically prevents ordering for times that have passed (e.g., can't order for 1 PM after 11 AM cutoff)
- **Multi-day Booking**: Customers can order up to 2 weeks in advance (configurable)
- **Responsive Popup**: Modern modal popup with date/time selection
- **Session Persistence**: Selected delivery time persists across page navigation
- **Automatic Validation**: Prevents checkout completion without delivery time selection

### 🛒 Checkout Integration
- Removed hardcoded delivery dropdown from checkout
- Seamless integration with existing order processing
- Maintains compatibility with existing delivery time storage

## Installation

1. Copy the plugin files to `/src/Plugins/Nop.Plugin.Company.Company/`
2. Build the solution or restart the application
3. Go to Admin → Configuration → Local Plugins
4. Install the "Company & Delivery Management" plugin
5. The global delivery date picker will automatically appear in the header

## Configuration

### Setting Up Delivery Time Slots

1. Go to Admin → Orders → Schedule Date
2. Configure delivery slots in the format: `Label-LastOrderTime-DeliveryTime`
3. Example configuration:
   ```
   Lunch-11:00:00-13:00:00,
   Afternoon-14:00:00-16:00:00,
   Dinner-17:00:00-19:00:00
   ```

### Time Format
- **Label**: Display name for the time slot (e.g., "Lunch", "Dinner")
- **LastOrderTime**: Latest time customers can place orders for this slot (HH:mm:ss)
- **DeliveryTime**: Actual delivery time (HH:mm:ss)

## How It Works

### For Customers

1. **Header Display**: Customers see a delivery time selector in the website header
2. **Initial State**: Shows "Select delivery time" if no time is chosen
3. **Time Selection**: Clicking opens a popup with available delivery slots
4. **Smart Filtering**: Only shows available time slots based on current time
5. **Visual Feedback**: Selected time is displayed prominently in the header
6. **Checkout Protection**: Cannot complete orders without selecting a delivery time

### For Store Owners

1. **Automatic Management**: Time slots are automatically filtered based on current time
2. **Order Integration**: Selected delivery times are saved with orders
3. **Flexible Configuration**: Easy to modify delivery schedules via admin panel
4. **Timezone Support**: Respects company-specific timezone settings

## Technical Details

### Components

- **`IDeliveryTimeService`**: Core service handling delivery time logic
- **`GlobalDeliveryDatePickerViewComponent`**: Header widget component
- **`DeliveryTimeController`**: AJAX endpoints for time selection
- **`DeliveryDatePickerWidgetProvider`**: Widget registration and injection

### Session Management

- Primary storage: Session state (`SelectedDeliveryTime`)
- Fallback storage: HTTP cookies (1-day expiration)
- Automatic cleanup of invalid selections

### Validation

- Real-time availability checking
- Cutoff time enforcement
- Multi-day range validation (up to 14 days ahead)
- Checkout-level validation with user-friendly error messages

## Customization

### Changing Max Days Ahead

Modify the `GetMaxDaysAhead()` method in `DeliveryTimeService.cs`:

```csharp
public virtual int GetMaxDaysAhead()
{
    return 14; // Change this value
}
```

### Styling the Header Component

The global picker uses CSS classes that can be customized:

- `.global-delivery-picker`: Main container
- `.delivery-display`: Header display element
- `.delivery-modal`: Popup modal
- `.delivery-time-slot`: Individual time slot buttons

### Automatic Popup Behavior

To show the popup automatically when no time is selected, uncomment this line in `Default.cshtml`:

```javascript
// modal.show(); // Uncomment this line
```

## Compatibility

- **NopCommerce Version**: 4.50+
- **Dependencies**: Existing OrderProcessingService and session management
- **Browser Support**: Modern browsers with JavaScript enabled

## Troubleshooting

### Common Issues

1. **Picker not appearing**: Ensure plugin is installed and widget zones are properly configured
2. **Time slots not showing**: Check ScheduleDate configuration in admin panel
3. **Checkout errors**: Verify delivery time is selected and still valid

### Debug Information

Check browser console for JavaScript errors and verify:
- Session state: Check developer tools → Application → Session Storage
- Cookies: Look for `SelectedDeliveryTime` cookie
- AJAX calls: Monitor network requests to `/DeliveryTime/` endpoints

## Support

For issues or customization requests, refer to the plugin source code or contact the development team.

