# Cloud Instance Performance Testing Tool

## Overview

The **Cloud Instance Performance Testing Tool** is an automation tool designed to simplify the process of comparing and testing virtual machines (VMs) across multiple cloud providers (AWS, Azure, and GCP). This project addresses the challenges of navigating cloud provider interfaces and comparing instance types by offering a streamlined solution for performance testing and analysis.

## Features

- **Multi-Cloud Support**: Automates the creation and performance testing of VMs on AWS, Azure, and GCP.
- **Secure Setup**: Collects and securely stores user account information for cloud providers in a `.env` file during an initial setup process.
- **Parallel Processing**: Runs three programs (`aws`, `azure`, and `gcp`) in parallel to handle the creation of necessary components (e.g., security groups, resource groups).
- **Performance Testing**: Executes tailored performance tests on created VMs and collects results.
- **Data Storage**: Stores test results in a cloud-hosted MongoDB database.
- **Website for Analysis**:
  - Displays normalized performance data and highlights the best-performing VMs.
  - Provides a table comparing cost-to-performance ratios to help users make informed decisions.
- **Custom Testing**: Allows users to specify and compare instances across providers with custom tests.
- **Cloud-Hosted Website**: Built with Node.js and Express.js, hosted on Azure for scalability and reliability.

## How It Works

1. **Initial Setup**:
   - The CLI application guides users through an initial setup where they input their cloud account information.
   - This information is securely stored in a `.env` file.

2. **Parallel Execution**:
   - The main CLI application runs three programs (`aws`, `azure`, `gcp`) in parallel.
   - These programs handle the creation of cloud components required to spin up VMs, such as security groups and resource groups.

3. **Performance Testing**:
   - The tool executes performance tests on created VMs, tailored to measure CPU, memory, and disk performance.
   - Results are collected and stored in a cloud-hosted MongoDB database.

4. **Data Visualization**:
   - A website provides a user-friendly interface to explore performance data.
   - Users can view normalized data, compare VM performance across providers, and analyze cost-to-performance ratios.
   - The website also supports running and displaying results of custom tests.

## Technologies Used

- **C#**: Core language for the CLI application and automation logic.
- **MongoDB**: Database for storing and querying performance test results.
- **Node.js and Express.js**: Backend framework for the website.
- **Azure**: Cloud hosting for the website and MongoDB database.

## Installation and Usage

### CLI Tool

1. Clone the repository:
    ```bash
    git clone https://github.com/edgarf25/cloud-performance-tool.git
    ```
2. Navigate into the project directory:
    ```bash
    cd cloud-performance-tool
    ```
3. Build and run the CLI application:
    ```bash
    dotnet build
    dotnet run
    ```
4. Follow the prompts for initial setup and provide cloud provider account information.

## Future Enhancements

- Add support for additional cloud providers.
- Integrate more detailed performance metrics, such as network latency and IOPS.
- Improve the UI/UX of the website for better data visualization.

## Screenshots

_Example of website interface displaying performance results and cost comparison table._

![Performance Results](link_to_screenshot)  
![Cost Comparison](link_to_screenshot)
