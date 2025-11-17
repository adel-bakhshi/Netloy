#!/bin/bash

# Sample shell script with input parameters
# Usage: example.sh [name] [age] [city]

echo "========================================"
echo "Welcome!"
echo "========================================"
echo ""

# Check if parameters exist
if [ -z "$1" ]; then
    echo "Error: Please provide your name!"
    echo "Usage: $(basename "$0") [name] [age] [city]"
    exit 1
fi

if [ -z "$2" ]; then
    echo "Error: Please provide your age!"
    echo "Usage: $(basename "$0") [name] [age] [city]"
    exit 1
fi

if [ -z "$3" ]; then
    echo "Error: Please provide your city!"
    echo "Usage: $(basename "$0") [name] [age] [city]"
    exit 1
fi

echo "Application Name: ${APP_BASE_NAME}"

# Print information
echo "First parameter (name): $1"
echo "Second parameter (age): $2"
echo "Third parameter (city): $3"
echo ""
echo "Hello $1! You are $2 years old and live in $3."
echo ""

echo "========================================"
echo "End of program"
echo "========================================"

# Optional: wait for user input (similar to pause in batch)
read -p "Press Enter to continue..."
