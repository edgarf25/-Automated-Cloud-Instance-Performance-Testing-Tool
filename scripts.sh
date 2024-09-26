#!/bin/bash

# Update the package list
sudo apt-get update

# Install sysbench
sudo apt-get install -y sysbench

# Run sysbench
sysbench cpu --cpu-max-prime=5000 run > /tmp/sysbench_output.txt

