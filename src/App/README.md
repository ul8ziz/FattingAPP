# Ul8ziz.FittingApp.App

WPF Application project for the Hearing Aid Fitting System.

## Project Structure

```
App/
├── App.xaml                 # Application entry point
├── App.xaml.cs
├── MainWindow.xaml          # Main window hosting MainView
├── MainWindow.xaml.cs
├── Program.cs               # Application entry point
├── Styles/
│   └── Styles.xaml          # ResourceDictionary with all styles
├── Views/
│   ├── MainView.xaml        # Main view with TopBar and Sidebar
│   └── MainView.xaml.cs
└── Properties/
    └── AssemblyInfo.cs
```

## Building the Project

1. Open the solution in Visual Studio 2022
2. Ensure .NET 7 SDK is installed
3. Build the solution (Ctrl+Shift+B)
4. Run the application (F5)

## Dependencies

- .NET 7.0
- WPF (Windows Presentation Foundation)
