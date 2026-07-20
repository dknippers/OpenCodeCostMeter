cmake_minimum_required(VERSION 3.25)
project(OpenCodeCostMeter VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

find_package(Qt6 6.8 REQUIRED COMPONENTS Core Gui Widgets Sql Test)
qt_standard_project_setup()

set(FONT_DIR "${CMAKE_CURRENT_BINARY_DIR}/fonts")
file(MAKE_DIRECTORY "${FONT_DIR}")
if(WIN32 AND EXISTS "C:/Windows/Fonts/CascadiaMono.ttf")
    configure_file("C:/Windows/Fonts/CascadiaMono.ttf" "${FONT_DIR}/CascadiaMono.ttf" COPYONLY)
else()
    file(DOWNLOAD "https://github.com/microsoft/cascadia-code/releases/download/v2407.24/CascadiaCode-2407.24.zip" "${CMAKE_CURRENT_BINARY_DIR}/CascadiaCode-2407.24.zip" STATUS cascadia_download TLS_VERIFY ON)
    list(GET cascadia_download 0 cascadia_status)
    if(NOT cascadia_status EQUAL 0)
        message(FATAL_ERROR "Unable to download Cascadia Code. Download it manually and place CascadiaMono.ttf in ${FONT_DIR}.")
    endif()
    file(ARCHIVE_EXTRACT INPUT "${CMAKE_CURRENT_BINARY_DIR}/CascadiaCode-2407.24.zip" DESTINATION "${CMAKE_CURRENT_BINARY_DIR}/cascadia")
    file(GLOB_RECURSE CASCADIA_MONO "${CMAKE_CURRENT_BINARY_DIR}/cascadia/*CascadiaMono*.ttf")
    if(NOT CASCADIA_MONO)
        message(FATAL_ERROR "Cascadia Code archive did not contain a Cascadia Mono TTF.")
    endif()
    list(GET CASCADIA_MONO 0 CASCADIA_MONO_FONT)
    configure_file("${CASCADIA_MONO_FONT}" "${FONT_DIR}/CascadiaMono.ttf" COPYONLY)
endif()
file(DOWNLOAD "https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip" "${CMAKE_CURRENT_BINARY_DIR}/Inter-4.1.zip" STATUS inter_download TLS_VERIFY ON)
list(GET inter_download 0 inter_status)
if(NOT inter_status EQUAL 0)
    message(FATAL_ERROR "Unable to download Inter 4.1. Download it manually and place Inter-Regular.ttf in ${FONT_DIR}.")
endif()
file(ARCHIVE_EXTRACT INPUT "${CMAKE_CURRENT_BINARY_DIR}/Inter-4.1.zip" DESTINATION "${CMAKE_CURRENT_BINARY_DIR}/inter")
file(GLOB_RECURSE INTER_REGULAR "${CMAKE_CURRENT_BINARY_DIR}/inter/*Inter-Regular.ttf")
if(NOT INTER_REGULAR)
    message(FATAL_ERROR "Inter 4.1 archive did not contain Inter-Regular.ttf.")
endif()
list(GET INTER_REGULAR 0 INTER_REGULAR_FONT)
configure_file("${INTER_REGULAR_FONT}" "${FONT_DIR}/Inter-Regular.ttf" COPYONLY)

qt_add_executable(OpenCodeCostMeter WIN32
    src/main.cpp
    src/models.h
    src/services.h src/services.cpp
    src/widget_window.h src/widget_window.cpp
    resources/app.qrc)
if(WIN32)
    target_sources(OpenCodeCostMeter PRIVATE resources/app.rc)
endif()
qt_add_resources(OpenCodeCostMeter "application_fonts" PREFIX "/fonts" BASE "${FONT_DIR}"
    FILES "${FONT_DIR}/CascadiaMono.ttf" "${FONT_DIR}/Inter-Regular.ttf")
target_link_libraries(OpenCodeCostMeter PRIVATE Qt6::Core Qt6::Gui Qt6::Widgets Qt6::Sql)

if(MSVC)
    target_compile_options(OpenCodeCostMeter PRIVATE /W4 /permissive-)
else()
    target_compile_options(OpenCodeCostMeter PRIVATE -Wall -Wextra -Wpedantic)
endif()

configure_file(${CMAKE_CURRENT_SOURCE_DIR}/resources/model-display-names.txt
    ${CMAKE_CURRENT_BINARY_DIR}/model-display-names.txt COPYONLY)
add_custom_command(TARGET OpenCodeCostMeter POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        ${CMAKE_CURRENT_SOURCE_DIR}/resources/model-display-names.txt
        $<TARGET_FILE_DIR:OpenCodeCostMeter>/model-display-names.txt)

include(GNUInstallDirs)
install(TARGETS OpenCodeCostMeter BUNDLE DESTINATION . RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})
install(FILES ${CMAKE_CURRENT_SOURCE_DIR}/resources/model-display-names.txt DESTINATION ${CMAKE_INSTALL_BINDIR})

enable_testing()
qt_add_executable(OpenCodeCostMeterTests tests/repository_tests.cpp src/models.h src/services.h src/services.cpp)
target_include_directories(OpenCodeCostMeterTests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_link_libraries(OpenCodeCostMeterTests PRIVATE Qt6::Core Qt6::Sql Qt6::Test)
add_test(NAME OpenCodeCostMeterTests COMMAND OpenCodeCostMeterTests)
