# Paycheck4 Project Coding Style Guide

## File Headers and Copyright
- Include copyright notice and license information
- Use region directives to encapsulate header sections
- Include comprehensive file description and purpose

## Code Organization
1. Using Directives:
   - Group system namespaces first
   - Follow with third-party namespaces
   - End with project-specific namespaces
   - Maintain alphabetical order within groups

2. Class Structure:
   - Use regions for logical grouping of members
   - Declare constants and static fields first
   - Follow with instance fields
   - Group properties together
   - Keep constructors after fields/properties
   - Order methods by access modifier (public, protected, private)

3. Field Naming:
   - Prefix private fields with underscore: `_fieldName`
   - Use camelCase for private fields
   - Use PascalCase for public properties
   - Constants should be PascalCase

4. Documentation:
   - Use XML documentation for public APIs
   - Include summary tags for all public members
   - Document parameters and return values
   - Include example usage where appropriate
   - Any time new files are added or files names or locations are changed, update the documentation index file accordingly (README)

5. Error Handling:
   - Use custom exception types for domain-specific errors
   - Include meaningful error messages
   - Maintain error code enumerations
   - Document error conditions

6. Enums:
   - Use [Flags] attribute for bit-field enums
   - Include XML documentation for each enum value
   - Group related values together
   - Use power-of-two values for flags enums

7. Protocol Implementation:
   - Document protocol specifications in comments
   - Include byte-level details for communications
   - Use const/readonly for protocol constants
   - Implement clear command parsing logic

8. Testing:
   - Create corresponding test files for each class
   - Group tests by functionality
   - Include both positive and negative test cases
   - Document test scenarios

## Formatting
- Use tabs for indentation
- Place opening braces on new lines
- Keep lines under 120 characters
- Use spaces around operators
- Add blank lines between logical sections

## Naming Conventions
1. Classes:
   - Use PascalCase
   - Include category in name (e.g., PaycheckEmulator, TclParser)
   - Suffix with purpose (Driver, Base, Core)

2. Methods:
   - Use PascalCase
   - Begin with verb (Get, Set, Calculate)
   - Include 'On' prefix for event handlers
   - Protected virtual methods prefixed with 'On'

3. Interfaces:
   - Prefix with 'I'
   - Use capability descriptions

4. Events:
   - Suffix with 'EventHandler'
   - Include sender and event args

## Best Practices
1. Implementation:
   - Prefer composition over inheritance
   - Use dependency injection where appropriate
   - Implement IDisposable for resource management
   - Use async/await for asynchronous operations

2. Comments:
   - Include purpose and implementation notes
   - Document complex algorithms
   - Explain protocol-specific details
   - Reference relevant documentation

3. Architecture:
   - Separate concerns (transport, protocol, business logic)
   - Use abstract base classes for common functionality
   - Implement interface segregation
   - Follow SOLID principles

4. Protocol Handling:
   - Use byte arrays for raw communication
   - Implement clear message framing
   - Include proper error detection
   - Support extensibility

## Security Considerations
- Validate all input data
- Handle sensitive data appropriately
- Implement proper error handling
- Use secure communication methods
- Document security-related code

## Performance Guidelines
- Use appropriate data structures
- Implement proper buffering
- Consider thread safety
- Optimize critical paths
- Document performance considerations

## Tech Stack
- C# as the primary programming language
- .NET framework for application development
- Use of standard libraries and NuGet packages
- Target platform is embedded (Raspberry Pi 5 running Debian 12)